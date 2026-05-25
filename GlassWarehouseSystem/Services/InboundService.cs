using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GlassWarehouseSystem.Config;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;
using GlassWarehouseSystem.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GlassWarehouseSystem.Services;

/// <summary>
/// 线程间测量数据传输对象。
/// 由生产者线程（MeasurementLoop）填充后放入队列，消费者线程（InboundExecutionLoop）取出使用。
/// ScannedId 存放的是已在数据库中成功匹配的 GlassID，Length/Width 为数据库中的精确尺寸。
/// </summary>
public class MeasurementData
{
    /// <summary>玻璃长度（来自数据库，单位 mm）</summary>
    public decimal Length { get; set; }
    /// <summary>玻璃宽度（来自数据库，单位 mm）</summary>
    public decimal Width { get; set; }
    /// <summary>已匹配的玻璃 GlassID（精确值，非 PLC 扫码原始值）</summary>
    public string? ScannedId { get; set; }
}

/// <summary>
/// 入库服务核心类，采用多线程生产者-消费者模式处理玻璃的自动入库流程。
/// 包含三条后台线程：
///   线程A（MeasurementLoop）  — 检测玻璃到达信号，读取尺寸/条码，匹配数据库，验证笼位，推入队列。
///   线程B（InboundExecutionLoop）— 从队列取出已验证数据，驱动 PLC 完成升降机定位与横推入笼。
///   线程C（GlobalSafetyLoop）  — 实时监听急停开关，触发时挂起 A/B 线程的执行。
/// </summary>
public class InboundService
{
    private readonly PlcService _plc;
    private readonly CageFinder _cageFinder;
    private readonly LogRepository _logRepository;

    private CancellationTokenSource? _cts;
    private BlockingCollection<MeasurementData>? _measurementQueue;

    // ======================== 系统配置参数（由 AppConfig 在 Start() 时读入）========================
    private float _measureError;        // 测量长宽时允许的最大误差范围（单位：mm）
    private int _dataSourceType;        // 数据获取模式：0=扫码，1=PLC 测量
    private int _maxScanRetry;          // 扫码/测量失败时的最大重试次数
    private float _glassSpacing;        // 入笼时相邻玻璃的安全间距（单位：mm）
    private int _pollTimeout;           // 等待 PLC 反馈的超时时长（单位：秒）
    private int _inboundRetryMax;       // 横推入笼超时时的最大重试次数
    private int _loopIdleDelayMs;       // 空闲轮询的休眠间隔（单位：ms）
    private int _errorRetryDelayMs;     // 发生错误后的延迟重试间隔（单位：ms）
    private int _noCageRetryDelayMs;    // 未找到可用笼位时的等待间隔（单位：ms）
    private int _pollIntervalMs;        // PLC 状态轮询的基础间隔（单位：ms）

    // ======================== 运行状态控制标志 ========================
    private volatile bool _stopRequested;   // 服务停止请求标志，设为 true 时各线程在当前操作结束后退出
    private volatile bool _isEStopActive;   // 急停激活标志，急停触发时所有动作线程挂起等待

    // ======================== 相机扫码 ========================
    private CameraScannerService? _scanner;         // 相机扫码服务实例（扫码模式下使用）
    private volatile string? _lastScannedBarcode;   // 后台相机线程扫到的最新条码，共享变量
    /// <summary>
    /// 标记相机是否已成功连接并启动。
    /// true  = 相机已就绪，ScanBarcode() 等待相机结果；
    /// false = 相机未就绪（连接失败或测量模式），ScanBarcode() 直接进入降级逻辑。
    /// </summary>
    private volatile bool _cameraAvailable;


    /// <summary>日志输出事件，订阅后可将运行日志推送至 UI 文本框。</summary>
    public Action<string>? OnLogActivity { get; set; }

    /// <summary>异常记录事件，匹配失败、PLC 超时等错误发生时会触发，推送至 UI 异常列表。</summary>
    public Action<string>? OnExceptionRecord { get; set; }

    /// <summary>比对记录事件，每次完成数据库尺寸匹配（无论成功或失败）时触发，推送至 UI 比对列表。</summary>
    public Action<string, string>? OnCompareRecord { get; set; }

    /// <summary>入库状态变更事件，物料状态修改后触发，通知 UI 刷新计划列表。</summary>
    public Action? OnInboundStatusChanged { get; set; }

    /// <summary>
    /// 获取流程卡扫码
    /// TODO
    /// </summary>
    public void InjectMockBarcode(string barcode)
    {
        _lastScannedBarcode = barcode;
        OnLogActivity?.Invoke($"模拟条码 '{barcode}' 已直接注入队列。");
    }


    /// <summary>默认构造函数，PLC、笼位查找器和日志仓库。</summary>
    public InboundService()
        : this(new PlcService(new PlcClient()), new CageFinder(new CageRepository()), new LogRepository())
    {
    }

    /// <summary>依赖注入构造函数，供单元测试注入 Mock 实现。</summary>
    public InboundService(PlcService plc, CageFinder cageFinder, LogRepository logRepository)
    {
        _plc = plc;
        _cageFinder = cageFinder;
        _logRepository = logRepository;
    }


    /// <summary>
    /// 启动入库服务。
    /// 从 AppConfig 加载所有运行参数，初始化队列和取消令牌，按需启动相机，
    /// 最后开启三条后台线程。
    /// </summary>
    public void Start()
    {
        OnLogActivity?.Invoke("入笼服务正在启动……");

        // 校验全局配置是否已初始化，防止参数缺失导致行为异常
        if (!AppConfig.IsInitialized)
        {
            throw new InvalidOperationException("全局配置 AppConfig 尚未初始化。");
        }

        // 读取业务参数和各线程延迟配置
        _measureError = AppConfig.GetFloat("MeasureErrorAllowance");
        _dataSourceType = AppConfig.GetInt("DataSourceType");
        _maxScanRetry = AppConfig.GetInt("MaxScanRetryTimes");
        _glassSpacing = AppConfig.GetFloat("GlassSpacing");
        _pollTimeout = AppConfig.GetInt("PlcPollTimeoutSeconds");
        _inboundRetryMax = AppConfig.GetInt("InboundRetryMax");
        _loopIdleDelayMs = AppConfig.GetInt("InboundLoopIdleDelayMs");
        _errorRetryDelayMs = AppConfig.GetInt("InboundErrorRetryDelayMs");
        _noCageRetryDelayMs = AppConfig.GetInt("InboundNoCageRetryDelayMs");
        _pollIntervalMs = AppConfig.GetInt("InboundPollIntervalMs");

        _stopRequested = false;
        _isEStopActive = false;

        // 清理上一次的取消令牌，重建队列（容量限为1，确保生产者在消费完前不能继续放入下一块）
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _measurementQueue = new BlockingCollection<MeasurementData>(new ConcurrentQueue<MeasurementData>(), 1);

        // 扫码模式（DataSourceType=0）才需要连接并启动物理相机
        _cameraAvailable = false;
        if (_dataSourceType == 0)
        {
            try
            {
                _scanner = new CameraScannerService();
                _scanner.OnBarcodeScanned += (s, barcode) => { _lastScannedBarcode = barcode; };
                _scanner.ConnectCamera();
                _scanner.StartScanning();
                _cameraAvailable = true; // 相机成功启动，ScanBarcode() 将等待真实扫码结果
                OnLogActivity?.Invoke("条码相机已连接并启动。");
            }
            catch (Exception ex)
            {
                // 相机模式下连接失败直接抛出，不允许降级运行
                // 调用方（InboundWindow）会捕获并显示错误，阻止服务启动
                throw new InvalidOperationException($"相机启动失败，请检查相机连接后重试：{ex.Message}", ex);
            }
        }

        // 服务启动时复位所有 PLC 指令/应答位，清除上次服务进程可能遗留的信号
        try
        {
            ResetInboundCommandBits();
            OnLogActivity?.Invoke("入笼服务：已复位所有 PLC 信号。");
        }
        catch (Exception ex)
        {
            OnLogActivity?.Invoke($"入笼服务：PLC 复位失败（{ex.Message}），服务仍将尝试启动。");
        }

        // 启动三条后台线程：安全监控（C）、测量生产（A）、入库消费（B）
        Task.Run(() => GlobalSafetyLoop(_cts.Token));
        Task.Run(() => MeasurementLoop(_cts.Token));
        Task.Run(() => InboundExecutionLoop(_cts.Token));
    }

    /// <summary>
    /// 停止入库服务。
    /// 设置停止标志，取消所有后台线程的令牌，释放相机资源。
    /// </summary>
    public void Stop()
    {
        _stopRequested = true;
        _cts?.Cancel();
        _scanner?.Dispose();
        OnLogActivity?.Invoke("入笼服务已停止。");
    }

    /// <summary>
    /// 将指定物料手动确认入库。
    /// 优先读取 PLC 当前层位（Addr_In_CurrentPos），反向映射到 A 笼实际层号，
    /// 确保记录的层与玻璃真实所在位置一致。
    /// PLC 读取失败或无法匹配时，降级调用 CageFinder 寻笼算法。
    /// </summary>
    public async Task<bool> ManualConfirmInboundAsync(string glassId, string? cageCode = null, int? layerNo = null, int? slotNo = null)
    {
        using var context = new WarehouseDbContext();
        using var transaction = await context.Database.BeginTransactionAsync();

        var material = await context.Materials.FirstOrDefaultAsync(m => m.GlassID == glassId);
        if (material == null) { await transaction.RollbackAsync(); return false; }

        // 已入库（InStock）的物料禁止重复手动入库，防止层容量被二次扣减
        if (material.Status == MaterialStatus.InStock)
        {
            await transaction.RollbackAsync();
            OnLogActivity?.Invoke($"手动入库拒绝：GlassID={glassId} 已处于 InStock 状态（笼={material.CurrentCage} 层={material.CurrentLayer}），无需重复入库。");
            return false;
        }

        Layer? layerEntity = null;

        if (string.IsNullOrEmpty(cageCode))
        {
            // 读 PLC 当前位置 → 反向映射到 A 笼实际层
            var mapped = TryMapPlcPositionToLayer();
            if (!mapped.HasValue)
            {
                OnLogActivity?.Invoke("手动入库失败：无法从 PLC 读取当前层位。");
                await transaction.RollbackAsync();
                return false;
            }
            cageCode    = mapped.Value.CageCode;
            layerNo     = mapped.Value.LayerNo;
            layerEntity = await context.Layers
                .FirstOrDefaultAsync(l => l.LayerID == mapped.Value.LayerID);
        }
        else if (layerNo.HasValue)
        {
            layerEntity = await context.Layers
                .FirstOrDefaultAsync(l => l.CageID == cageCode && l.LayerNo == layerNo.Value);
        }

        // ── 计算槽号（与 FinalizeInboundTransaction 相同：Max+1）──
        if (!slotNo.HasValue && !string.IsNullOrEmpty(cageCode) && layerNo.HasValue)
        {
            var maxSlot = await context.Materials
                .Where(x => x.CurrentCage == cageCode && x.CurrentLayer == layerNo && x.InboundTime != null)
                .MaxAsync(x => (int?)x.SlotNo) ?? 0;
            slotNo = maxSlot + 1;
        }

        // ── 更新物料入库信息 ──
        material.Status      = MaterialStatus.InStock;
        material.InboundTime = DateTime.Now;
        if (!string.IsNullOrEmpty(cageCode)) material.CurrentCage  = cageCode;
        if (layerNo.HasValue)                material.CurrentLayer = layerNo.Value;
        if (slotNo.HasValue)                 material.SlotNo       = slotNo.Value;

        // ── 更新层剩余容量（与 FinalizeInboundTransaction 完全一致）──
        if (layerEntity != null)
        {
            if (layerEntity.RemainingLength == null)
            {
                var cageLen = await context.Cages
                    .Where(c => c.CageCode == cageCode)
                    .Select(c => c.Length)
                    .FirstOrDefaultAsync();
                layerEntity.RemainingLength = (double?)(cageLen) ?? 0.0;
            }
            layerEntity.RemainingLength -= (double)(material.Length + Convert.ToDecimal(_glassSpacing));
            layerEntity.IsOccupied       = true;
            layerEntity.GlassSpec        = $"{material.Length}×{material.Width}";
            layerEntity.Width            = (double)material.Width;
            layerEntity.GlassID          = glassId;
        }

        context.Logs.Add(new SystemLog
        {
            LogContent = $"手动入库。GlassID={glassId}，笼号={cageCode ?? "未分配"}，层号={layerNo?.ToString() ?? "-"}，槽位={slotNo?.ToString() ?? "-"}",
            RecordTime = DateTime.Now,
            Type       = "Info"
        });

        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        ClearPlanCache();
        CageRepository.InvalidateCache();
        OnInboundStatusChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 读取 PLC 当前传送台位置（Addr_In_CurrentPos），反向映射到 A 笼的笼号和层号。
    /// 映射公式（与 CageFinder.BuildResult 正向公式对应）：
    ///   expectedPos = GridStartCoord + (LayerNo - 1) × (layer.Space ?? cage.InboundGap)
    /// 位置误差在 PosTolerance（mm）以内时视为命中。
    /// </summary>
    private (string CageCode, int LayerNo, string LayerID)? TryMapPlcPositionToLayer()
    {
        const float PosTolerance = 15f; // 允许误差 15 mm
        try
        {
            var currentPos = _plc.ReadFloat("Addr_In_CurrentPos");
            var cages = new CageRepository().GetOnlineCagesWithLayers()
                .Where(c => c.LocationType?.StartsWith("A", StringComparison.OrdinalIgnoreCase) == true)
                .OrderBy(c => c.CageCode);

            foreach (var cage in cages)
            {
                var gridStart = (float)(cage.GridStartCoord ?? 0m);
                foreach (var layer in cage.Layers.OrderBy(l => l.LayerNo ?? 0))
                {
                    var layerNo = layer.LayerNo ?? 0;
                    if (layerNo <= 0) continue;
                    // 优先用 Layer.Coord（DB 预存坐标）；该字段为 null 时降级计算
                    float expectedPos = layer.Coordinate.HasValue
                        ? (float)layer.Coordinate.Value
                        : gridStart + (layerNo - 1) * (float)(layer.Space ?? (double?)cage.InboundGap ?? 0.0);
                    if (Math.Abs(currentPos - expectedPos) <= PosTolerance)
                    {
                        OnLogActivity?.Invoke(
                            $"手动入库：PLC当前位置={currentPos:F1}mm，匹配到 {cage.CageCode} 第{layerNo}层（期望={expectedPos:F1}mm）");
                        return (cage.CageCode, layerNo, layer.LayerID);
                    }
                }
            }
            OnLogActivity?.Invoke(
                $"手动入库：PLC当前位置={currentPos:F1}mm，未匹配到任何 A 笼层。");
        }
        catch (Exception ex)
        {
            OnLogActivity?.Invoke($"手动入库：读取 PLC 当前位置失败（{ex.Message}）。");
        }
        return null;
    }

    /// <summary>
    /// 手动干预：将指定物料状态标记为"损坏"。
    /// </summary>
    public async Task<bool> MarkAsDamagedAsync(string glassId)
    {
        using var context = new WarehouseDbContext();
        var material = await context.Materials.FirstOrDefaultAsync(m => m.GlassID == glassId);

        // 物料不存在时直接返回失败
        if (material == null) return false;

        material.Status = MaterialStatus.Damaged;
        material.IsDamaged = true;
        await context.SaveChangesAsync();
        ClearPlanCache();
        return true;
    }

    /// <summary>清除物料和笼位相关缓存，在写入操作后调用确保下次读取为最新数据。</summary>
    public void ClearPlanCache()
    {
        // 直接按已知 key 删除，避免 GetServer().Keys() 模式匹配在部分 Redis 配置下静默失败
        CacheQueryService.ClearCacheByKeys(
            "plan:materials:all",
            "plan:materials:pending",
            "cages:online_with_layers");
    }


    /// <summary>
    /// 【线程A - 测量】
    /// 持续轮询 PLC 的玻璃到达信号（Addr_In_GlassArrived）。
    /// 检测到到达信号（值=1）后：
    ///   1. 读取玻璃尺寸或扫码 ID（TryGetGlassSize）。
    ///   2. 在数据库中按 ID 或尺寸容差匹配对应的 Pending 物料。
    ///   3. 验证是否存在在线 A 笼，以及玻璃尺寸是否超过所有 A 笼的最大容纳尺寸。
    ///   4. 全部通过后将数据库中的精确尺寸和 GlassID 放入队列，并复位到位信号（写0）。
    ///   5. 匹配失败或校验不通过时，向 PLC 写入比较错误标志（Addr_In_CompareError=true）。
    /// </summary>
    private void MeasurementLoop(CancellationToken token)
    {
        // 只要未触发整体服务取消，循环持续运行
        while (!token.IsCancellationRequested)
        {
            // 收到服务停止请求时退出线程
            if (_stopRequested) break;

            try
            {
                // 急停激活时挂起，避免误操作
                if (_isEStopActive)
                {
                    Thread.Sleep(_pollIntervalMs);
                    continue;
                }

                // TryReadBool：通信失败时返回 false（不抛异常），避免污染日志
                if (!_plc.TryReadBool("Addr_In_GlassArrived", out var arrived) || !arrived)
                {
                    // 空闲等待
                    Thread.Sleep(_loopIdleDelayMs);
                    continue;
                }
                OnLogActivity?.Invoke($"[PLC流] ==============================================");
                    OnLogActivity?.Invoke($"[PLC流] 1. 发现 Addr_In_GlassArrived 变为 1，玻璃已到达！");

                    // --- 步骤2：获取玻璃识别特征（条码 ID 或 PLC 测量尺寸）---
                    // 扫码模式：在本轮扫描开始前清除上一块玻璃留下的残留条码。
                    // 相机后台线程持续运行，处理完上一块玻璃后仍会继续扫到同一玻璃并
                    // 覆写 _lastScannedBarcode，若不清除会导致本轮立即返回旧条码。
                    if (_dataSourceType == 0)
                        _lastScannedBarcode = null;
                    OnLogActivity?.Invoke($"[PLC流] 2. 正在提取玻璃识别特征 (扫码 或 {AppConfig.GetInt("DataSourceType")} 模式)...");
                    if (!TryGetGlassSize(out var glassLength, out var glassWidth, out var glassId))
                    {
                        // 扫码/测量失败：通知 PLC 异常并复位信号，放行当前玻璃，等待下一块
                        OnLogActivity?.Invoke("扫码/测量失败，已通知 PLC 异常并复位信号");
                        _plc.WriteBool("Addr_In_CompareError", true);
                        _plc.WriteBool("Addr_In_GlassArrived", false);
                        Thread.Sleep(_errorRetryDelayMs);
                        continue;
                    }

                    OnLogActivity?.Invoke(string.IsNullOrEmpty(glassId)
                        ? $"[PLC流] 2. 测量结果: L={glassLength}, W={glassWidth}，正在数据库匹配..."
                        : $"[PLC流] 2. 扫码结果: GlassID={glassId}，正在数据库匹配...");

                    // 通知 PLC 扫码/测量已完成
                    _plc.WriteBool("Addr_In_ScanOK", true);
                    OnLogActivity?.Invoke("[PLC流] 2.1. 已写入 Addr_In_ScanOK = true");

                    // --- 步骤3：数据库匹配（扫码=精确匹配 ID，测量=容差匹配尺寸）---
                    // 通过 CacheQueryService 获取待入库物料列表（命中缓存时无需访问 MySQL）
                    var pendingList = CacheQueryService.GetCachedData<Material>(
                        "plan:materials:pending",
                        () =>
                        {
                            using var ctx = new WarehouseDbContext();
                            return ctx.Materials.Where(m => m.Status == MaterialStatus.Pending).ToList();
                        },
                        TimeSpan.FromSeconds(30)
                    );
                    Material? existingMaterial = null;

                    if (!string.IsNullOrEmpty(glassId))
                    {
                        // 扫码模式：直接用条码 ID 精确查找 Pending 物料
                        existingMaterial = pendingList.FirstOrDefault(m => m.GlassID == glassId);
                    }
                    else
                    {
                        // 测量模式：在允许误差范围内匹配长宽（测量误差仅用于匹配，不传入队列）
                        existingMaterial = pendingList.FirstOrDefault(m =>
                            Math.Abs((double)m.Length - (double)glassLength) <= _measureError &&
                            Math.Abs((double)m.Width - (double)glassWidth) <= _measureError);
                    }

                    if (existingMaterial == null)
                    {
                        // 匹配失败：记录异常日志并向 PLC 写入比较错误码
                        var errStr = string.IsNullOrEmpty(glassId) ?
                            $"匹配失败：尺寸 L={glassLength}, W={glassWidth}, 容差={_measureError}" :
                            $"匹配失败：数据库无处于 Pending 且 ID={glassId} 的记录";
                        OnLogActivity?.Invoke(errStr);
                        LogAndEmitException(errStr, "Error");
                        OnCompareRecord?.Invoke(string.IsNullOrEmpty(glassId) ? $"L={glassLength},W={glassWidth}" : glassId, "未匹配");

                        // 向 PLC 写入比较错误信号，通知现场异常
                        _plc.WriteBool("Addr_In_CompareError", true);
                        // 复位 ScanOK 和到位信号，避免 PLC 侧状态残留
                        _plc.WriteBool("Addr_In_ScanOK", false);
                        _plc.WriteBool("Addr_In_GlassArrived", false);
                    }
                    else
                    {
                        OnCompareRecord?.Invoke(string.IsNullOrEmpty(glassId) ? $"L={glassLength},W={glassWidth}" : glassId, "匹配成功");

                        // 取数据库中的精确尺寸（忽略测量误差），确保入库计算使用准确数据
                        var matchedId = existingMaterial.GlassID;
                        var materialLength = existingMaterial.Length;
                        var materialWidth = existingMaterial.Width;

                        // --- 步骤4：前置安全校验 ---

                        if (_cageFinder.IsGlassOversizeForAllACages(materialLength, materialWidth, out var oversizeMsg))
                        {
                            // 玻璃尺寸超过所有 A 笼的最大容纳尺寸，标记错误并报警
                            LogAndEmitException(oversizeMsg, "Error");
                            MarkMaterialAsError(matchedId);
                            _plc.WriteBool("Addr_In_CompareError", true);
                            // 复位 ScanOK 和到位信号，避免 PLC 侧状态残留
                            _plc.WriteBool("Addr_In_ScanOK", false);
                            _plc.WriteBool("Addr_In_GlassArrived", false);
                        }
                        else
                        {
                            // 清除上一轮可能残留的错误标志，确保 PLC 侧状态干净
                            _plc.WriteBool("Addr_In_CompareError", false);

                            // --- 步骤4.1：将数据库中的精确长宽写入 PLC，供硬件侧显示或校验 ---
                            _plc.WriteFloat("Addr_In_GlassLength", (float)materialLength);
                            _plc.WriteFloat("Addr_In_GlassWidth", (float)materialWidth);
                            OnLogActivity?.Invoke($"[PLC流] 4.1. 已写入玻璃尺寸 -> Addr_In_GlassLength={materialLength}, Addr_In_GlassWidth={materialWidth}");

                            // --- 步骤5：校验通过，数据入队并复位到位信号 ---
                            // 队列中传递的是数据库精确值（而非 PLC 实测值），测量误差仅用于匹配阶段
                            _measurementQueue?.Add(new MeasurementData
                            {
                                Length = materialLength,
                                Width = materialWidth,
                                ScannedId = matchedId
                            }, token);

                            // 复位 PLC 到位地址和扫码完成信号，通知现场玻璃已被系统接管
                            _plc.WriteBool("Addr_In_GlassArrived", false);
                            _plc.WriteBool("Addr_In_ScanOK", false);
                            OnLogActivity?.Invoke("数据正确入列，已复位 Addr_In_GlassArrived 和 Addr_In_ScanOK");
                        }
                    }

                    // 阻塞等待，直到 Addr_In_GlassArrived 恢复为 false（玻璃离开检测区域）后再进入下一轮
                    // TryReadBool 失败时 value=false，会立即 break；实际上等同于通信中断时放行，是可接受的安全降级。
                    while (!token.IsCancellationRequested)
                    {
                        if (_isEStopActive) break;
                        _plc.TryReadBool("Addr_In_GlassArrived", out var stillHere);
                        if (!stillHere) break;
                        Thread.Sleep(_loopIdleDelayMs);
                    }
            }
            catch (OperationCanceledException)
            {
                // 收到 Stop() 发出的取消信号，正常退出线程
                break;
            }
            catch (Exception ex)
            {
                // 捕获未预期的异常，记录后延迟继续运行，避免线程崩溃
                LogAndEmitException($"测量线程异常: {ex.Message}", "Error");
                Thread.Sleep(_errorRetryDelayMs);
            }
        }
    }

    /// <summary>
    /// 【线程B - 入库消费者】
    /// 从队列中取出已通过验证的测量数据（精确 GlassID + 数据库尺寸），
    /// 再次从数据库加载物料实体后，调用 ProcessOneInboundAction 驱动 PLC 完成入库。
    /// </summary>
    private void InboundExecutionLoop(CancellationToken token)
    {
        // 只要未触发整体服务取消，循环持续运行
        while (!token.IsCancellationRequested)
        {
            // 收到服务停止请求时退出线程
            if (_stopRequested) break;

            try
            {
                // 急停激活时挂起，等待恢复
                if (_isEStopActive)
                {
                    Thread.Sleep(_pollIntervalMs);
                    continue;
                }

                // 尝试从队列取出一条已验证的入库任务
                if (_measurementQueue == null || !_measurementQueue.TryTake(out var data, 500, token))
                {
                    continue;
                }

                // 队列中的 ScannedId 必须是有效的 GlassID
                var matchedId = data.ScannedId;
                if (string.IsNullOrEmpty(matchedId)) continue;

                // 实时查询数据库，确认物料仍处于 Pending 状态
                // 不使用缓存，防止线程A放入队列后物料被手动操作（标记破损/手动入库）而状态已变更
                Material? existingMaterial;
                using (var verifyCtx = new WarehouseDbContext())
                {
                    existingMaterial = verifyCtx.Materials
                        .FirstOrDefault(m => m.GlassID == matchedId && m.Status == MaterialStatus.Pending);
                }

                // 物料已被其他操作更改状态（手动标记破损、手动入库等），跳过本轮入库
                if (existingMaterial == null)
                {
                    OnLogActivity?.Invoke($"[跳过] GlassID={matchedId} 已不是Pending状态（可能已被手动操作处理），放弃本次入库。");
                    continue;
                }

                // 驱动 PLC 执行完整的物理入库动作序列
                ProcessOneInboundAction(existingMaterial, data.Length, data.Width, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogAndEmitException($"入笼执行线程异常: {ex.Message}", "Error");
                Thread.Sleep(_errorRetryDelayMs);
            }
        }
    }

    /// <summary>
    /// 执行单次完整入库动作序列（PLC 升降机定位 → 横推入笼 → 等待到位 → 等待完成 → 落盘）。
    /// 流程：
    ///   3. 调用寻笼算法，获取目标笼层及 PLC 位置坐标。
    ///   4. 写入目标位置，等待升降机到位反馈（Addr_In_PosReached=1）。
    ///   5. 发出横推指令（Addr_In_EnterCmd=1）。
    ///   6. 等待入笼到位反馈（Addr_In_EnterReached=1）。
    ///  6.1. 等待横推完成反馈（Addr_In_EnterDone=1）。
    ///   7. 落盘数据库，更新物料状态为已入库，更新层剩余容量。
    /// </summary>
    private void ProcessOneInboundAction(Material material, decimal measureLength, decimal measureWidth, CancellationToken token)
    {
        var glassId = material.GlassID;

        // 规格字符串和寻笼均使用数据库订单中的额定尺寸（material.Length/Width），
        // 而非传感器测量值（measureLength/measureWidth）。
        // 测量值存在传感器误差，同一规格的不同玻璃可能测出不同数值，
        // 若用测量值构建 GlassSpec 会导致同规格玻璃无法匹配到同一层。
        var nominalLength = material.Length != 0 ? material.Length : measureLength;
        var nominalWidth  = material.Width  != 0 ? material.Width  : measureWidth;
        var glassSpec = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{nominalLength}×{nominalWidth}");

        // --- 步骤3：寻笼——从数据库寻找最优可用笼层 ---
        var cageResult = _cageFinder.FindCage(nominalLength, nominalWidth, out var cageSearchReason);

        if (cageResult == null)
        {
            // FindCage 返回详细原因（全满 / 无匹配层 / 无在线A笼）
            LogAndEmitException($"未找到可用笼位。GlassID={glassId}，原因={cageSearchReason}", "Warning");
            // 通知 PLC 本次入库失败，防止 PLC 侧持续等待上位机指令
            _plc.WriteBool("Addr_In_CompareError", true);
            ResetInboundCommandBits();
            MarkMaterialAsError(glassId);
            Thread.Sleep(_noCageRetryDelayMs);
            return;
        }

        var retryCount = 0;

        // 开启重试循环，发生局部超时时允许有限次原位重试
        while (!token.IsCancellationRequested)
        {
            // --- 步骤3.读：读取当前传送台位置（计算差值必需） ---
            float currentPos;
            try
            {
                currentPos = _plc.ReadFloat("Addr_In_CurrentPos");
                OnLogActivity?.Invoke($"[PLC流] 3. 查架成功！当前传送台位置 = {currentPos} mm，目标层标高 = {cageResult.TargetPosValue}");
            }
            catch (Exception ex)
            {
                OnLogActivity?.Invoke($"[PLC流] 3. 当前传送台位置读取失败，无法计算差值，中止入笼：{ex.Message}");
                ResetInboundCommandBits();
                MarkMaterialAsError(glassId);
                return;
            }

            // --- 步骤3.写：写入目标层与当前位置的差值至 PLC ---
            float posOffset = (float)cageResult.TargetPosValue - currentPos;
            OnLogActivity?.Invoke($"[PLC流] 3.1. 写入移动差值 -> Addr_In_TargetCagePos = {posOffset:F1} (目标={cageResult.TargetPosValue} - 当前={currentPos:F1})");
            if (!_plc.WriteFloat("Addr_In_TargetCagePos", posOffset))
            {
                LogAndEmitException($"PLC 写入目标层位失败! Addr_In_TargetCagePos={cageResult.TargetPosValue}, GlassID={glassId}", "Error");
                ResetInboundCommandBits();
                MarkMaterialAsError(glassId);
                return;
            }

            // --- 步骤4：等待升降机移动到位（Addr_In_PosReached=1）---
            OnLogActivity?.Invoke($"[PLC流] 4. 等待升降机反馈到位 -> 持续读取 Addr_In_PosReached，期望值为 1...");
            if (!WaitForBool("Addr_In_PosReached", _pollTimeout, token, out var timeoutMessage))
            {
                LogAndEmitException($"入笼定位失败。GlassID={glassId}，原因={timeoutMessage}", "Error");
                ResetInboundCommandBits();
                MarkMaterialAsError(glassId);
                return;
            }

            // --- 步骤5：发出横推入笼指令（Addr_In_EnterCmd=1）---
            OnLogActivity?.Invoke($"[PLC流] 5. 机器已就位！命令横推！-> 写入 Addr_In_EnterCmd = 1");
            if (!_plc.WriteBool("Addr_In_EnterCmd", true))
            {
                LogAndEmitException($"PLC 写入横推指令失败! Addr_In_EnterCmd=1, GlassID={glassId}", "Error");
                ResetInboundCommandBits();
                MarkMaterialAsError(glassId);
                return;
            }

            // --- 步骤6：等待横推完成信号（Addr_In_EnterDone=1）---
            OnLogActivity?.Invoke($"[PLC流] 6. 等待横推完成反馈 -> 持续读取 Addr_In_EnterDone，期望值为 1...");
            if (!WaitForBool("Addr_In_EnterDone", _pollTimeout, token, out timeoutMessage))
            {
                retryCount++;
                LogAndEmitException($"入笼动作超时。GlassID={glassId}，重试次数={retryCount}，原因={timeoutMessage}", "Error");
                ResetInboundCommandBits();

                // 超过最大重试次数，认为机械卡滞，放弃本次入库
                if (retryCount >= _inboundRetryMax)
                {
                    LogAndEmitException($"入笼重试次数已达上限，放弃本次入笼。GlassID={glassId}", "Error");
                    MarkMaterialAsError(glassId);
                    break;
                }
                continue;
            }

            // --- 步骤7：动作闭环，落盘数据库并复位 PLC 指令位 ---
            OnLogActivity?.Invoke($"[PLC流] 7. 动作闭环！落盘数据库并重置指令位 (EnterCmd=0, TargetCagePos=0)。完成入库！");
            FinalizeInboundTransaction(glassId, measureWidth, glassSpec, cageResult);
            ResetInboundCommandBits();
            return;
        }
    }


    /// <summary>
    /// 将物料状态更新为 Error，仅对仍处于 Pending 状态的物料生效，防止重复覆盖。
    /// 用于匹配失败、无可用笼、超限等异常场景。
    /// </summary>
    private void MarkMaterialAsError(string glassId)
    {
        try
        {
            using var context = new WarehouseDbContext();
            var material = context.Materials.FirstOrDefault(m => m.GlassID == glassId && m.Status == MaterialStatus.Pending);

            if (material != null)
            {
                material.Status = MaterialStatus.Error;
                context.SaveChanges();
                ClearPlanCache();
            }
            OnInboundStatusChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LogAndEmitException($"标记物料为异常状态失败。GlassID={glassId}，原因={ex.Message}", "Error");
        }
    }

    /// <summary>
    /// 根据配置的数据获取模式，读取玻璃的尺寸信息或扫码 ID，支持重试机制。
    ///   DataSourceType=0：扫码模式，调用 ScanBarcode() 获取条码 ID（length/width 保持 0）。
    ///   DataSourceType=1：测量模式，从 PLC 光幕读取长宽数值（glassId 保持 null）。
    /// </summary>
    private bool TryGetGlassSize(out decimal length, out decimal width, out string? glassId)
    {
        length = 0m;
        width = 0m;
        glassId = null;

        for (var i = 0; i < _maxScanRetry; i++)
        {
            try
            {
                // 扫码模式：等待相机扫码返回条码 ID，该 ID 即为数据库中的 GlassID
                if (_dataSourceType == 0)
                {
                    glassId = ScanBarcode();
                    return !string.IsNullOrEmpty(glassId);
                }

                // 测量模式：从 PLC 寄存器读取实测长宽
                if (_dataSourceType == 1)
                {
                    (length, width) = MeasureGlass();
                    return true;
                }

                // 非法 DataSourceType 配置，不可重试
                LogAndEmitException($"非法的 DataSourceType 配置={_dataSourceType}", "Error");
                return false;
            }
            catch (Exception ex)
            {
                LogAndEmitException($"获取玻璃尺寸失败。重试 {i + 1}/{_maxScanRetry}，原因={ex.Message}", "Error");
                Thread.Sleep(_pollIntervalMs);
            }
        }

        // 达到最大重试次数，放弃本轮获取
        LogAndEmitException("达到最大重试次数，获取玻璃尺寸失败。", "Error");
        return false;
    }

    /// <summary>
    /// 轮询等待后台相机线程扫到有效条码（最多 5 秒）。
    /// 相机后台线程每帧轮询耗时约 1000ms，5s 等待时间保证至少 4-5 个帧周期。
    /// 超时后返回空串，由调用方（TryGetGlassSize）触发重试。
    /// </summary>
    private string ScanBarcode()
    {
        var start = DateTime.UtcNow;
        // 轮询等待，最多 5 秒（相机每帧轮询需 1000ms，至少需 2-3 个帧周期）
        while ((DateTime.UtcNow - start).TotalSeconds < 5.0)
        {
            if (!string.IsNullOrEmpty(_lastScannedBarcode))
            {
                var code = _lastScannedBarcode;
                // 获取后立即清空，防止下次误读旧条码
                _lastScannedBarcode = null;
                return code;
            }
            Thread.Sleep(50);
        }

        // 超时未扫到条码，返回空串由上层重试，不降级
        OnLogActivity?.Invoke("[警告] 相机在 5 秒内未识别到有效条码，本轮放弃，等待重试。");
        return string.Empty;
    }

    /// <summary>
    /// 从 PLC 的 Addr_In_GlassLength 和 Addr_In_GlassWidth 读取实测尺寸，转换为 decimal 返回。
    /// 读取值必须大于 0，否则抛出异常触发重试。
    /// </summary>
    private (decimal Length, decimal Width) MeasureGlass()
    {
        var length = _plc.ReadFloat("Addr_In_GlassLength");
        var width = _plc.ReadFloat("Addr_In_GlassWidth");

        // 校验读取值是否在有效物理范围内
        if (length <= 0 || width <= 0)
        {
            throw new InvalidOperationException($"非法的 PLC 测量值: L={length}, W={width}");
        }

        // 读取完成后清零 PLC 中的长宽值，防止下次误读旧数据
        _plc.WriteFloat("Addr_In_GlassLength", 0);
        _plc.WriteFloat("Addr_In_GlassWidth", 0);

        // PLC 传来的已是 mm 单位的标定值，直接转换为 decimal
        // 若 PLC 传来单位不同（如 0.1mm），在此处添加系数转换
        return (Convert.ToDecimal(length), Convert.ToDecimal(width));
    }

    /// <summary>
    /// 轮询等待指定 PLC Bool 地址变为 true，直到超时。
    /// 读取到 true 时返回 true；超时、取消或急停时返回 false 并附带原因说明。
    /// </summary>
    private bool WaitForBool(string keyName, int timeoutSeconds, CancellationToken token, out string message)
    {
        var start = DateTime.UtcNow;

        while ((DateTime.UtcNow - start).TotalSeconds < timeoutSeconds)
        {
            if (token.IsCancellationRequested)
            {
                message = "Canceled";
                return false;
            }

            if (_isEStopActive)
            {
                message = "EStop";
                return false;
            }

            try
            {
                if (_plc.ReadBool(keyName))
                {
                    message = "OK";
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                OnLogActivity?.Invoke($"[WaitForBool] 读取 {keyName} 通信失败，将继续重试...");
            }
            Thread.Sleep(_pollIntervalMs);
        }

        message = $"{keyName} timeout";
        return false;
    }

    /// <summary>
    /// 将物理入库完成的结果写入数据库（事务保护）：
    ///   - 更新物料状态为 InStock，记录入库时间和库位（笼号、层号、槽号）。
    ///   - 扣减目标层的剩余容量（长度 + 间距）。
    ///   - 写入系统操作日志。
    /// 仅允许针对 LocationType="A笼" 的目标执行，其他情况回滚并报错。
    /// </summary>
    private void FinalizeInboundTransaction(string glassId, decimal glassWidth, string glassSpec, FindCageResult result)
    {
        using var context = new WarehouseDbContext();
        using var transaction = context.Database.BeginTransaction();

        var material = context.Materials.FirstOrDefault(x => x.GlassID == glassId && x.Status == MaterialStatus.Pending);
        var layer = context.Layers.FirstOrDefault(x => x.LayerID == result.LayerID);

        // 操作期间物料或层实体不应被外部移除
        if (material == null || layer == null)
        {
            transaction.Rollback();
            LogAndEmitException($"入笼落盘失败：未找到对应物料或笼层。GlassID={glassId}", "Error");
            return;
        }

        // 业务规则校验：入库通道必须为 A笼 类型（StartsWith("A") 与 CageFinder.IsInboundCage 保持一致）
        if (string.IsNullOrWhiteSpace(result.LocationType)
            || !result.LocationType.StartsWith("A", StringComparison.OrdinalIgnoreCase))
        {
            transaction.Rollback();
            LogAndEmitException($"入笼落盘失败：LocationType={result.LocationType}，应为 A 笼类型。", "Error");
            return;
        }

        // 使用 Max+1 计算槽号，避免并发时槽号冲突
        var maxSlot = context.Materials
            .Where(x => x.CurrentCage == result.CageCode && x.CurrentLayer == result.LayerNo && x.InboundTime != null)
            .Max(x => (int?)x.SlotNo) ?? 0;
        var slotNo = maxSlot + 1;

        // 更新物料入库信息
        material.Status = MaterialStatus.InStock;
        material.CurrentCage = result.CageCode;
        material.CurrentLayer = result.LayerNo;
        material.SlotNo = slotNo;
        material.InboundTime = DateTime.Now;

        // 扣减层剩余可用长度：RemainingLength 为 null 时以笼总长初始化，避免首次入库时从0开始计算出负值
        if (layer.RemainingLength == null)
        {
            var cageLength = context.Cages
                .Where(c => c.CageCode == result.CageCode)
                .Select(c => c.Length)
                .FirstOrDefault();
            layer.RemainingLength = (double?)(cageLength) ?? 0.0;
        }
        layer.RemainingLength -= (double)(material.Length + Convert.ToDecimal(_glassSpacing));
        layer.IsOccupied = true;
        layer.GlassID = glassId;
        layer.GlassSpec = glassSpec;
        layer.Width = (double)material.Width; // 使用数据库精确宽度

        context.Logs.Add(new SystemLog
        {
            LogContent = $"入笼成功。GlassID={glassId}，笼号={result.CageCode}，层号={result.LayerNo}，槽位={slotNo}",
            RecordTime = DateTime.Now,
            Type = "Info"
        });

        context.SaveChanges();
        transaction.Commit();
        ClearPlanCache();
        OnLogActivity?.Invoke($"入笼完成。GlassID={glassId}，笼号={result.CageCode}，层号={result.LayerNo}");
        OnInboundStatusChanged?.Invoke();
    }

    /// <summary>
    /// 复位 PLC 入库所有指令位和应答位。
    /// 入库服务启动时、每次动作结束、超时或中止后均需调用。
    /// 应答位（PosReached / EnterDone / CompareError）必须在 PC 确认后清零，
    /// 否则下一块玻璃的 WaitForBool 会因残留信号立即返回 true。
    /// </summary>
    private void ResetInboundCommandBits()
    {
        _plc.WriteBool("Addr_In_EnterCmd",      false); // PC→PLC 指令位：横推指令
        _plc.WriteFloat("Addr_In_TargetCagePos", 0f);   // PC→PLC 指令位：目标层位差值
        _plc.WriteBool("Addr_In_ScanOK",        false); // PC→PLC 指令位：扫码完成确认
        _plc.WriteBool("Addr_In_PosReached",    false); // 应答位：升降机到位
        _plc.WriteBool("Addr_In_EnterDone",     false); // 应答位：横推入笼完成
        _plc.WriteBool("Addr_In_CompareError",  false); // 应答位：比对错误标志
    }

    /// <summary>
    /// 【线程C - 安全监控】
    /// 持续轮询 PLC 急停开关地址（Addr_GlobalEStop=0 表示急停激活）。
    /// 急停状态变化时输出日志。
    /// </summary>
    private void GlobalSafetyLoop(CancellationToken token)
    {
        var lastState = false;
        var failCount = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                //   - 读取成功且值=false → PLC真实反馈急停已按下（低电平有效）
                //   - 读取失败返回false → PLC未连接，默认值false不代表急停，不应误判为acute stop
                if (_plc.TryReadBool("Addr_GlobalEStop", out var eStopVal))
                {
                    // PLC 通信正常：根据真实值判断急停状态
                    _isEStopActive = !eStopVal;
                    failCount = 0; // 通信成功则重置连续失败计数

                    // 急停状态发生变化时输出诊断日志和状态变更日志
                    if (_isEStopActive != lastState)
                    {
                        OnLogActivity?.Invoke($"[诊断] Addr_GlobalEStop 读取值={eStopVal}，判定急停状态={_isEStopActive}");
                        lastState = _isEStopActive;

                        if (_isEStopActive)
                        {
                            LogAndEmitException("急停已触发，入笼流程暂停", "Warning");
                            OnLogActivity?.Invoke("急停已触发。");
                        }
                        else
                        {
                            _logRepository.Insert("急停已释放，入笼流程恢复", "Info");
                            OnLogActivity?.Invoke("急停已释放。");
                        }
                    }
                }
                else
                {
                    // PLC 读取失败（未连接或通信中断）：计入失败次数，不将默认值0误判为急停
                    failCount++;
                    if (failCount >= 3)
                    {
                        _isEStopActive = true;
                        if (failCount == 3) // 仅第一次触发阈值时记录，避免日志刷屏
                            LogAndEmitException(
                                $"PLC 连接失败（连续 {failCount} 次读取Addr_GlobalEStop无响应），已触发软急停，入库暂停。",
                                "Error");
                    }
                }
            }
            catch
            {
                failCount++;
                if (failCount >= 3)
                {
                    // 捕获到意外异常，同样触发软急停保护现场安全
                    _isEStopActive = true;
                    if (failCount == 3)
                        LogAndEmitException("PLC 连续通信失败，已触发软急停。", "Error");
                }
            }

            Thread.Sleep(_pollIntervalMs);
        }
    }

    /// <summary>
    /// 将错误信息同时写入数据库日志和 UI 异常列表（通过 OnExceptionRecord 事件）。
    /// </summary>
    private void LogAndEmitException(string msg, string type = "Error")
    {
        _logRepository.Insert(msg, type);
        OnExceptionRecord?.Invoke($"[{type}] {msg}");
    }
}
