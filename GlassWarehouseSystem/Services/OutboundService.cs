using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GlassWarehouseSystem.Config;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace GlassWarehouseSystem.Services;

/// <summary>
/// 出笼服务。
/// 不依赖订单驱动或计划驱动，直接将所有"在库"状态的玻璃按 config 表中的排序配置
/// 生成出笼队列，逐片驱动 PLC 完成出笼动作。
///
/// PLC 出笼流程：
///   1. 写 Addr_Out_StartCmd=1，写 Addr_Out_MoveDistance=移动距离（带符号，mm）
///   2. 监听 Addr_Out_PosReached=1 → 笼到位
///   3. 置 Addr_Out_PopCmd=1 → 开始出片；监听 Addr_Out_PopDone=1 → 出片完成
///   4. 监听 Addr_Out_Status：1=正常→归档并打印，2=故障→检查 Addr_GlobalEStop，急停=1则回到步骤1
///
/// 出库完成后：从 Materials 表删除该记录，写入 HistoryMaterials 表归档。
/// </summary>
public class OutboundService
{
    private CancellationTokenSource? _cts;
    private volatile bool _stopRequested;

    /// <summary>
    /// 急停激活标志，由 GlobalSafetyLoop 独立线程写入，出笼各步骤只读不写。
    /// true  = 急停按下或 PLC 连续失联，所有 PLC 操作立即终止并等待。
    /// false = 正常运行，出笼流程可继续执行。
    /// </summary>
    private volatile bool _isEStopActive;

    // 时间参数在 Start 时从 AppConfig（config 表）读入，禁止在此处硬编码
    private int _pollIntervalMs;
    private int _pollTimeoutMs;
    private int _safetyPollIntervalMs;
    private int _outboundRetryMax;

    /// <summary>日志输出事件</summary>
    public Action<string>? OnLogActivity { get; set; }

    /// <summary>
    /// 单项出笼完成回调。参数：materialId, success, moveDistance。
    /// </summary>
    public Action<string, bool, float>? OnOutboundItemCompleted { get; set; }

    /// <summary>
    /// 打印标签回调。出笼归档成功后 Invoke，参数为已归档前的完整物料对象（含 Order 导航）。
    /// 由 OutboundWindow 订阅，在 UI 线程打开打印窗口。
    /// </summary>
    public Action<Material>? OnPrintTrigger { get; set; }


    /// <summary>
    /// 从数据库加载所有在库物料，并根据 config 表中的排序配置进行排序。
    /// config 键：
    ///   OutSortField  — 排序字段：Length / Width（默认 Length）
    ///   OutSortDir    — 排序方向：Asc / Desc（默认 Desc）
    /// </summary>
    public async Task<List<Material>> LoadInStockMaterialsSortedAsync()
    {
        // 读取 config 中的排序配置
        string sortField = AppConfig.GetStringOrDefault("OutSortField", "Length");
        string sortDir = AppConfig.GetStringOrDefault("OutSortDir", "Desc");

        return await CacheQueryService.GetCachedDataAsync<Material>(
            "outbound:materials:instock",
            async () =>
            {
                using var context = new WarehouseDbContext();

                // 出笼只从 B 笼出货，先取所有 B 笼编码
                var bCageCodes = context.Cages
                    .Where(c => c.LocationType != null && c.LocationType.StartsWith("B"))
                    .Select(c => c.CageCode)
                    .ToList();

                IQueryable<Material> query = context.Materials
                    .Include(m => m.Order)
                    .Where(m => m.Status == MaterialStatus.InStock && bCageCodes.Contains(m.CurrentCage));

                bool byLength = sortField.Equals("Length", StringComparison.OrdinalIgnoreCase);
                bool descending = sortDir.Equals("Desc", StringComparison.OrdinalIgnoreCase);

                query = (byLength, descending) switch
                {
                    (true, true)   => query.OrderByDescending(m => m.Length).ThenBy(m => m.CurrentCage).ThenBy(m => m.CurrentLayer),
                    (true, false)  => query.OrderBy(m => m.Length).ThenBy(m => m.CurrentCage).ThenBy(m => m.CurrentLayer),
                    (false, true)  => query.OrderByDescending(m => m.Width).ThenBy(m => m.CurrentCage).ThenBy(m => m.CurrentLayer),
                    (false, false) => query.OrderBy(m => m.Width).ThenBy(m => m.CurrentCage).ThenBy(m => m.CurrentLayer),
                };

                return await query.ToListAsync();
            },
            TimeSpan.FromMinutes(2)
        );
    }

    //  单片出库：删除物料 + 归档历史 

    /// <summary>
    /// 完成单片出库：
    ///   1. 将物料信息写入 HistoryMaterials 表。
    ///   2. 释放该层空间（Layer.IsOccupied=false, GlassID=null）。
    ///   3. 从 Materials 表中删除该记录。
    ///   4. 写入系统日志。
    /// </summary>
    public async Task<bool> ProcessOutboundAsync(string materialId)
    {
        using var context = new WarehouseDbContext();
        var material = await context.Materials
            .Include(m => m.Order)
            .FirstOrDefaultAsync(x => x.GlassID == materialId);
        if (material == null)
            return false;

        // 1. 归档到历史表
        context.HistoryMaterials.Add(new HistoryMaterial
        {
            GlassID        = material.GlassID,
            OrderID        = material.OrderID,
            ProductName    = material.ProductName,
            Length         = material.Length,
            Width          = material.Width,
            Thickness      = material.Thickness,
            OriginalStatus = material.Status,
            IsDamaged      = material.IsDamaged,
            CageCode       = material.CurrentCage,
            LayerNo        = material.CurrentLayer,
            SlotNo         = material.SlotNo,
            InboundTime    = material.InboundTime,
            OutboundTime   = DateTime.Now,
            OrderName      = material.OrderName,
            OrderNo        = material.Order?.OrderNo,
            FlowCardNo     = material.Order?.FlowCardNo,
            CustomerName   = material.Order?.CustomerName,
            GroupID        = material.GroupID
        });

        // 2. 释放层空间
        var layer = await context.Layers
            .FirstOrDefaultAsync(x => x.CageID == material.CurrentCage && x.LayerNo == material.CurrentLayer);
        if (layer != null)
        {
            // 恢复该层剩余容量（长度 + 间距）
            float glassSpacing = AppConfig.GetFloat("GlassSpacing");
            layer.RemainingLength = (layer.RemainingLength ?? 0)
                                  + (double)(material.Length + Convert.ToDecimal(glassSpacing));

            // 检查该层是否还有其他物料（排除当前正在出笼的）
            var otherMaterialsOnLayer = await context.Materials
                .AnyAsync(m => m.CurrentCage == material.CurrentCage
                            && m.CurrentLayer == material.CurrentLayer
                            && m.GlassID != material.GlassID);

            if (!otherMaterialsOnLayer)
            {
                // 最后一片，完全释放层
                layer.IsOccupied = false;
                layer.GlassID = null;
                layer.GlassSpec = null;
                layer.Width = null;
            }
            else
            {
                // 还有其他片，将 GlassID 更新为同层其中一片的 ID，保持层记录有效
                var nextGlassId = await context.Materials
                    .Where(m => m.CurrentCage == material.CurrentCage
                             && m.CurrentLayer == material.CurrentLayer
                             && m.GlassID != material.GlassID)
                    .Select(m => m.GlassID)
                    .FirstOrDefaultAsync();
                layer.GlassID = nextGlassId;
            }
        }

        // 3. 从物料表中删除
        context.Materials.Remove(material);

        // 4. 日志
        context.Logs.Add(new SystemLog
        {
            RecordTime = DateTime.Now,
            Type = "信息",
            LogContent = $"出笼完成并归档 GlassID={material.GlassID}"
        });

        await context.SaveChangesAsync();

        // 出库完成后清除相关缓存，确保下次加载到最新数据
        CacheQueryService.ClearCacheByKeys(
            "outbound:materials:instock",
            "plan:materials:all",
            "display:cages:all",
            "display:layers:all");

        return true;
    }

    /// <summary>
    /// 启动 PLC 驱动的自动出笼循环。
    /// </summary>
    public void StartOutboundLoop(PlcService plc, List<OutboundTaskItemViewModel> pendingItems)
    {
        _stopRequested = false;
        _isEStopActive = false;
        LoadConfig();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // 线程C（安全监控）：独立轮询急停地址，与入笼保持相同的监听架构
        Task.Run(() => GlobalSafetyLoop(plc, token));

        Task.Run(async () =>
        {
            // 读取 PLC 实际当前位置作为起点，避免手动出笼后起点偏差
            // 读取失败时持续重试，不允许以 0 代替（否则第一片 moveDistance 完全错误）
            float lastPos = 0f;
            while (!token.IsCancellationRequested && !_stopRequested)
            {
                try
                {
                    lastPos = plc.ReadFloat("Addr_Out_CurrentPos");
                    Log($"[自动出笼] 起始位置 = {lastPos:F1} mm");
                    // 服务启动时复位所有 PLC 信号，清除上次进程可能遗留的指令/应答位
                    ResetOutboundCmd(plc);
                    Log("[自动出笼] PLC 信号已复位，进入出笼队列处理...");
                    break;
                }
                catch (Exception ex)
                {
                    // 暴露真实异常信息，便于排查 PLC 连接 / Modbus 地址解析 / 数据格式等问题
                    Log($"[自动出笼] 读取 Addr_Out_CurrentPos 失败: {ex.GetType().Name}: {ex.Message}，等待重试...");
                    await Task.Delay(_pollIntervalMs, token);
                }

            }
            if (token.IsCancellationRequested || _stopRequested) return;

            // 重排：保证同 GroupID 的物料在执行序列中连续出现，其他排序相对位置不变
            int countBeforeReorder = pendingItems.Count;
            pendingItems = ReorderForGroups(pendingItems);
            Log($"[自动出笼] 队列长度: 传入={countBeforeReorder}, 重排后={pendingItems.Count}");
            for (int idx = 0; idx < pendingItems.Count; idx++)
                Log($"  待处理[{idx + 1}]: MaterialID={pendingItems[idx].MaterialID}, GroupID={pendingItems[idx].GroupID ?? "(无)"}, TargetPos={pendingItems[idx].TargetPos:F1}");

            for (int i = 0; i < pendingItems.Count; i++)
            {
                if (_stopRequested || token.IsCancellationRequested)
                {
                    Log($"出笼循环被用户停止 (已处理 {i}/{pendingItems.Count})");
                    break;
                }

                // 急停激活时挂起，与入笼相同模式
                if (_isEStopActive)
                {
                    Log("急停激活，出笼暂停，等待恢复...");
                    await Task.Delay(_pollIntervalMs, token);
                    i--; // 当前片未开始，不推进索引
                    continue;
                }

                var item = pendingItems[i];

                float targetPos = item.TargetPos;
                float moveDistance = targetPos - lastPos;

                var groupTag = string.IsNullOrEmpty(item.GroupID) ? "" : $" [连组 {item.GroupID}]";
                Log($"[{i + 1}/{pendingItems.Count}]{groupTag} 开始出笼: {item.MaterialID}, " +
                    $"笼位={targetPos:F1}, 移动距离={moveDistance:F1}");

                bool success = false;
                try
                {
                    success = await ExecuteSingleOutbound(plc, item.MaterialID, targetPos, moveDistance, lastPos, token);
                }
                catch (Exception ex)
                {
                    Log($"出笼异常: {item.MaterialID} - {ex.Message}");
                }

                if (success)
                {
                    try
                    {
                        Log($"  归档中: {item.MaterialID}...");
                        bool dbOk = await ProcessOutboundAsync(item.MaterialID);
                        if (!dbOk)
                        {
                            Log($"  ERROR: 数据库归档失败（物料可能已不存在）: {item.MaterialID}");
                            success = false;
                        }
                        else
                        {
                            lastPos = targetPos;
                            Log($"出笼完成: {item.MaterialID}");
                            // item.Material 在内存中仍有完整 Order 导航属性，归档后 DB 中已删除
                            if (item.Material != null)
                                OnPrintTrigger?.Invoke(item.Material);
                        }
                    }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException?.Message ?? "";
                        Log($"  ERROR: 归档异常 {item.MaterialID} - {ex.Message}" +
                            (string.IsNullOrEmpty(inner) ? "" : $" | 内部: {inner}"));
                        success = false;
                    }
                }
                else
                {
                    Log($"出笼失败: {item.MaterialID}");
                }

                OnOutboundItemCompleted?.Invoke(item.MaterialID, success, moveDistance);
            }

            Log("=== 出笼循环结束 ===");
        }, token);
    }

    /// <summary>停止出笼循环</summary>
    public void Stop()
    {
        _stopRequested = true;
        _cts?.Cancel();
    }

    // 手动单片出笼（含 PLC 交互）

    /// <summary>
    /// 手动出笼：读取 PLC 当前位置，计算到目标层的距离，执行与自动出笼相同的 PLC 交互流程，
    /// 成功后调用 ProcessOutboundAsync 完成数据库归档。
    /// </summary>
    /// <param name="plc">PLC 服务实例</param>
    /// <param name="materialId">要出笼的 GlassID</param>
    /// <param name="targetPos">目标层笼位坐标（由 CalcTargetPos 计算）</param>
    /// <returns>true=出笼并归档成功；false=失败</returns>
    public async Task<bool> ManualOutboundAsync(PlcService plc, string materialId, float targetPos)
    {
        // 手动出笼独立运行，不受上一次自动出笼 Stop() 的影响
        _stopRequested = false;
        _isEStopActive = false;
        LoadConfig();

        using var cts = new CancellationTokenSource();

        // 启动安全监控线程（与自动出笼相同架构）
        _ = Task.Run(() => GlobalSafetyLoop(plc, cts.Token));

        try
        {
            // 读取 PLC 当前笼位坐标
            float currentPos;
            try
            {
                currentPos = plc.ReadFloat("Addr_Out_CurrentPos");
                Log($"[手动] 当前位置 = {currentPos:F1} mm，目标 = {targetPos:F1} mm");
                // 手动出笼启动时同样复位，确保干净初态
                ResetOutboundCmd(plc);
            }
            catch (Exception ex)
            {
                Log($"[手动] 读取 Addr_Out_CurrentPos 失败，中止: {ex.Message}");
                return false;
            }

            float moveDistance = targetPos - currentPos;
            Log($"[手动] 移动距离 = {moveDistance:F1} mm，开始 PLC 流程...");

            bool plcOk = await ExecuteSingleOutbound(plc, materialId, targetPos, moveDistance, currentPos, cts.Token);
            if (!plcOk)
            {
                Log($"[手动] PLC 流程失败: {materialId}");
                return false;
            }

            // 归档前先加载物料（ProcessOutboundAsync 会将其从 Materials 表删除）
            Material? matForPrint;
            using (var ctx = new WarehouseDbContext())
                matForPrint = await ctx.Materials.Include(m => m.Order)
                    .FirstOrDefaultAsync(m => m.GlassID == materialId);

            // PLC 流程成功 → 数据库归档
            bool dbOk = await ProcessOutboundAsync(materialId);
            Log(dbOk ? $"[手动] 归档成功: {materialId}" : $"[手动] 归档失败: {materialId}");
            if (dbOk && matForPrint != null) OnPrintTrigger?.Invoke(matForPrint);
            return dbOk;
        }
        finally
        {
            cts.Cancel();
        }
    }

    //  PLC 单片出笼交互 

    /// <summary>
    /// 执行单片出笼的完整 PLC 交互流程。
    /// 
    /// 流程：
    ///   1. 写 Addr_Out_StartCmd=1，写 Addr_Out_MoveDistance=移动距离（带符号，mm）
    ///   2. 监听 Addr_Out_PosReached=1 笼到位
    ///   3. 置 Addr_Out_PopCmd=1 出片；监听 Addr_Out_PopDone=1 出片完成
    ///   4. 监听 Addr_Out_Status：1=正常，2=故障
    ///      故障时：检查 Addr_GlobalEStop=1 则回到步骤1重试
    /// </summary>
    private async Task<bool> ExecuteSingleOutbound(
        PlcService plc, string materialId, float targetPos, float moveDistance, float lastPos, CancellationToken token)
    {
        // 故障重试外层循环：如果 Addr_Out_Status=2 且急停恢复，则从头开始
        int maxRetries = _outboundRetryMax;
        for (int retry = 0; retry <= maxRetries; retry++)
        {
            if (_stopRequested || token.IsCancellationRequested) return false;

            if (retry > 0)
                Log($"  第 {retry} 次重试出笼: {materialId}");

            // 每次尝试前先复位所有信号，确保干净初态
            ResetOutboundCmd(plc);

            // 步骤1: 写移动距离、启动指令
            plc.WriteFloat("Addr_Out_MoveDistance", moveDistance);
            plc.WriteBool("Addr_Out_StartCmd", true);
            Log($"  [{materialId}] 步骤1: 移动 {moveDistance:+0.0;-0.0} mm → 等待笼到位...");

            // 步骤2: 监听 Addr_Out_PosReached=1（笼到位）
            if (!await WaitForBool(plc, "Addr_Out_PosReached", true, token))
            {
                Log($"  ERROR [{materialId}] 步骤2: 等待笼位到位超时");
                ResetOutboundCmd(plc);
                return false;
            }

            // 步骤3: 置 Addr_Out_PopCmd=1 开始出片
            plc.WriteBool("Addr_Out_PopCmd", true);
            Log($"  [{materialId}] 步骤3: 笼到位，推片中 → 等待出片完成...");
            if (!await WaitForBool(plc, "Addr_Out_PopDone", true, token))
            {
                Log($"  ERROR [{materialId}] 步骤3: 等待出片完成超时");
                ResetOutboundCmd(plc);
                return false;
            }
            Log($"  [{materialId}] 步骤3: 出片完成");

            // 步骤4: 监听 Addr_Out_Status 运行状态
            Log($"  [{materialId}] 步骤4: 等待 Addr_Out_Status=1...");
            bool fault = false;
            try
            {
                if (await WaitForInt(plc, "Addr_Out_Status", 1, token))
                {
                    Log($"  [{materialId}] 步骤4: Addr_Out_Status=1，正常完成");
                    ResetOutboundCmd(plc);
                    return true;
                }

                int status = plc.ReadInt("Addr_Out_Status");
                if (status == 2)
                {
                    fault = true;
                    Log($"  WARNING [{materialId}] 步骤4: Addr_Out_Status=2 故障，等待 Addr_GlobalEStop 恢复...");
                }
                else
                {
                    Log($"  [{materialId}] 步骤4: Addr_Out_Status={status}，视为完成");
                    ResetOutboundCmd(plc);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"  [{materialId}] 步骤4: 读取 Addr_Out_Status 异常({ex.Message})，视为完成");
                ResetOutboundCmd(plc);
                return true;
            }

            // 故障处理：Addr_Out_Status=2 时复位指令位并等待急停恢复（由 GlobalSafetyLoop 负责检测）
            if (fault)
            {
                ResetOutboundCmd(plc);
                Log("  Addr_Out_Status=2 故障，等待 Addr_GlobalEStop 恢复后重试...");

                // 等待急停标志由安全线程清除（_isEStopActive → false），再回到步骤1重试
                int waitElapsed = 0;
                while (_isEStopActive && !_stopRequested && !token.IsCancellationRequested)
                {
                    await Task.Delay(_pollIntervalMs, token);
                    waitElapsed += _pollIntervalMs;
                    if (waitElapsed >= _pollTimeoutMs)
                    {
                        Log("  ERROR: 等待急停恢复超时");
                        return false;
                    }
                }

                if (!_isEStopActive)
                {
                    Log("  故障已消除，回到步骤1重试出笼");
                    continue; // 回到 for 循环顶部，即回到步骤1
                }
            }
        }

        Log($"  ERROR: 出笼重试 {maxRetries} 次仍失败: {materialId}");
        ResetOutboundCmd(plc);
        return false;
    }

    // ======================== 安全监控线程 ========================

    /// <summary>
    /// 从 AppConfig 加载出笼服务的运行时参数（必须在 AppConfig.Initialize 之后调用）。
    /// </summary>
    private void LoadConfig()
    {
        _pollIntervalMs       = AppConfig.GetInt("OutboundPollIntervalMs");
        _pollTimeoutMs        = AppConfig.GetInt("OutboundPollTimeoutMs");
        _safetyPollIntervalMs = AppConfig.GetInt("OutboundSafetyPollIntervalMs");
        _outboundRetryMax     = AppConfig.GetInt("OutboundRetryMax");
    }

    private void GlobalSafetyLoop(PlcService plc, CancellationToken token)
    {
        var lastState = false;
        var failCount = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (plc.TryReadBool("Addr_GlobalEStop", out var eStopVal))
                {
                    // EStop=1 表示正常，EStop=0 表示急停激活
                    _isEStopActive = !eStopVal;
                    failCount = 0;

                    if (_isEStopActive != lastState)
                    {
                        lastState = _isEStopActive;
                        if (_isEStopActive)
                            Log("[安全] 急停触发 (Addr_GlobalEStop=0)，出笼暂停");
                        else
                            Log("[安全] 急停释放 (Addr_GlobalEStop=1)，出笼恢复");
                    }
                }
                else
                {
                    // PLC 读取失败，不将默认值误判为急停，但连续失败则触发软急停
                    failCount++;
                    if (failCount >= 3)
                    {
                        _isEStopActive = true;
                        if (failCount == 3)
                            Log($"[安全] PLC 连续 {failCount} 次读取 Addr_GlobalEStop 失败，触发软急停");
                    }
                }
            }
            catch
            {
                failCount++;
                if (failCount >= 3)
                {
                    _isEStopActive = true;
                    if (failCount == 3)
                        Log("[安全] PLC 通信异常，触发软急停");
                }
            }

            Thread.Sleep(_safetyPollIntervalMs);
        }
    }

    // ======================== PLC 辅助方法 ========================

    /// <summary>
    /// 将出笼队列重排：同 GroupID 的成员在序列中紧挨着出现，其余非连组片以及各组首位
    /// 之间仍保持调用方传入的原始排序。连组成员需要在 PLC 上顺序出笼（每片都走完整流程），
    /// 中途不能被其他片插入，否则笼位来回移动会破坏连组物理连续性。
    /// </summary>
    private static List<OutboundTaskItemViewModel> ReorderForGroups(List<OutboundTaskItemViewModel> items)
    {
        var result        = new List<OutboundTaskItemViewModel>(items.Count);
        var visitedItems  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            // 注意：这里只判断"是否已加入 result"，不能在此处把 MaterialID 写入 visitedItems，
            // 否则下面的同组遍历会把当前 item 自身误判为"已访问"而跳过，导致丢件。
            if (visitedItems.Contains(item.MaterialID)) continue;

            if (!string.IsNullOrEmpty(item.GroupID))
            {
                if (!visitedGroups.Add(item.GroupID)) continue;

                // 把本组所有成员按原顺序一次性追加（含当前 item 自身）
                foreach (var member in items.Where(x => x.GroupID == item.GroupID))
                {
                    if (visitedItems.Add(member.MaterialID))
                        result.Add(member);
                }
            }
            else
            {
                if (visitedItems.Add(item.MaterialID))
                    result.Add(item);
            }
        }

        return result;
    }

    /// <summary>
    /// 复位所有出笼 PLC 信号至初始值，避免残留值影响下一片出笼的等待判断。
    /// 应在每片流程结束后调用，也应在每片流程开始前调用以确保干净初态。
    /// </summary>
    private void ResetOutboundCmd(PlcService plc)
    {
        try
        {
            plc.WriteBool("Addr_Out_StartCmd",  false); // PC→PLC 指令位：停止启动
            plc.WriteBool("Addr_Out_PopCmd",    false); // PC→PLC 指令位：停止推片
            // 应答位：PC 读取并确认这些信号后将其清零。
            // 必须清零，否则下一片的 WaitForBool 会因残留信号立即返回 true。
            plc.WriteBool("Addr_Out_PosReached", false); // 应答：笼到位确认
            plc.WriteBool("Addr_Out_PopDone",    false); // 应答：出片完成确认
            plc.WriteInt("Addr_Out_Status",      0);     // 应答：状态寄存器归零
            // 注意：Addr_Out_MoveDistance 不在此处清零，笼位置寄存器应保留最后一次写入值
        }
        catch (Exception ex)
        {
            Log($"  复位指令异常: {ex.Message}");
        }
    }

    /// <summary>轮询等待 Bool 地址变为期望值，急停激活时立即返回 false（与 InboundService.WaitForBool 相同策略）</summary>
    private async Task<bool> WaitForBool(PlcService plc, string address, bool expected, CancellationToken token)
    {
        int elapsed = 0;
        while (elapsed < _pollTimeoutMs)
        {
            if (_stopRequested || token.IsCancellationRequested) return false;
            if (_isEStopActive)
            {
                Log($"  [WaitForBool] 急停激活，终止等待 {address}");
                return false;
            }
            try
            {
                if (plc.TryReadBool(address, out bool val) && val == expected)
                    return true;
            }
            catch { }
            await Task.Delay(_pollIntervalMs, token);
            elapsed += _pollIntervalMs;
        }
        return false;
    }

    /// <summary>轮询等待 Int 地址变为期望值，急停激活时立即返回 false</summary>
    private async Task<bool> WaitForInt(PlcService plc, string address, int expected, CancellationToken token)
    {
        int elapsed = 0;
        while (elapsed < _pollTimeoutMs)
        {
            if (_stopRequested || token.IsCancellationRequested) return false;
            if (_isEStopActive)
            {
                Log($"  [WaitForInt] 急停激活，终止等待 {address}");
                return false;
            }
            try
            {
                int val = plc.ReadInt(address);
                if (val == expected) return true;
            }
            catch { }
            await Task.Delay(_pollIntervalMs, token);
            elapsed += _pollIntervalMs;
        }
        return false;
    }

    private void Log(string message)
    {
        OnLogActivity?.Invoke(message);
    }
}
