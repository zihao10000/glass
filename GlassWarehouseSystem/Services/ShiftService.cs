using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;
using GlassWarehouseSystem.Repositories;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GlassWarehouseSystem.Services;

public sealed class ShiftService
{
    private readonly PlcService _plc;
    private readonly LogRepository _logRepository;

    public ShiftService()
        : this(new PlcService(new PlcClient()), new LogRepository())
    {
    }

    public ShiftService(PlcService plc, LogRepository logRepository)
    {
        _plc = plc;
        _logRepository = logRepository;
    }

    public async Task<ShiftResult> ShiftAToBAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var context = new WarehouseDbContext();
            var aCage = await context.Cages.AsNoTracking()
                .FirstOrDefaultAsync(c => c.LocationType != null && c.LocationType.StartsWith("A"), cancellationToken);
            var bCage = await context.Cages.AsNoTracking()
                .FirstOrDefaultAsync(c => c.LocationType != null && c.LocationType.StartsWith("B"), cancellationToken);

            if (aCage == null || bCage == null)
            {
                return ShiftResult.Fail("未找到 A 笼或 B 笼，请先配置 cages.LocationType。");
            }

            var orderCheck = await CheckOrderConsistencyAsync(context, aCage.CageCode, bCage.CageCode, cancellationToken);
            if (!orderCheck.IsMatch)
            {
                return ShiftResult.Fail($"顺移前订单校验失败: {orderCheck.Message}");
            }

            // 先提交 DB 数据（A→B），再触发 PLC 硬件动作
            // 顺序保证：若 DB 失败则物理笼不动；若 PLC 信号失败数据已正确，可安全重试
            var moved = await MoveCageDataAsync(aCage.CageCode, bCage.CageCode, cancellationToken);

            // DB 提交成功后，通知 PLC 执行物理顺移
            _plc.WriteBool("Addr_CageShiftReq", true);

            _logRepository.Insert($"顺移完成 A={aCage.CageCode} -> B={bCage.CageCode} 移动数量={moved}", "信息");
            return ShiftResult.Ok(moved, aCage.CageCode, bCage.CageCode);
        }
        catch (Exception ex)
        {
            _logRepository.Insert($"顺移异常: {ex.Message}", "错误");
            return ShiftResult.Fail(ex.Message);
        }
    }

    private static async Task<(bool IsMatch, string Message)> CheckOrderConsistencyAsync(
        WarehouseDbContext context,
        string aCageCode,
        string bCageCode,
        CancellationToken cancellationToken)
    {
        var aOrders = await context.Materials.AsNoTracking()
            .Where(m => m.CurrentCage == aCageCode && m.InboundTime != null && m.Status != MaterialStatus.Outbounded)
            .Select(m => m.OrderID ?? string.Empty)
            .Distinct()
            .ToListAsync(cancellationToken);

        var bOrders = await context.Materials.AsNoTracking()
            .Where(m => m.CurrentCage == bCageCode && m.InboundTime != null && m.Status != MaterialStatus.Outbounded)
            .Select(m => m.OrderID ?? string.Empty)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (aOrders.Count == 0)
        {
            return (false, "A 笼没有可顺移物料。");
        }

        if (bOrders.Count == 0)
        {
            return (true, "B 笼为空，允许顺移。");
        }

        // B 笼已有玻璃（无论是不是同一订单），物理上会冲突 —— 拒绝顺移
        var bGlassCount = await context.Materials.AsNoTracking()
            .CountAsync(m => m.CurrentCage == bCageCode
                          && m.InboundTime != null
                          && m.Status != MaterialStatus.Outbounded,
                          cancellationToken);
        return (false,
            $"B 笼内已有 {bGlassCount} 片玻璃（订单: {string.Join(",", bOrders)}），" +
            "顺移会造成物理冲突。请先将 B 笼出库清空再执行顺移。");
    }


    private static async Task<int> MoveCageDataAsync(string aCageCode, string bCageCode, CancellationToken cancellationToken)
    {
        using var context = new WarehouseDbContext();
        using var tx = await context.Database.BeginTransactionAsync(cancellationToken);

        var moveMaterials = await context.Materials
            .Where(m => m.CurrentCage == aCageCode && m.InboundTime != null && m.Status != MaterialStatus.Outbounded)
            .ToListAsync(cancellationToken);

        foreach (var m in moveMaterials)
        {
            m.CurrentCage = bCageCode;
        }

        var aLayers = await context.Layers.Where(l => l.CageID == aCageCode).ToListAsync(cancellationToken);
        var bLayers = await context.Layers.Where(l => l.CageID == bCageCode).ToListAsync(cancellationToken);
        var bByLayerNo = bLayers.ToDictionary(l => l.LayerNo ?? 0, l => l);

        var aCageLength = await context.Cages
            .Where(c => c.CageCode == aCageCode)
            .Select(c => c.Length)
            .FirstOrDefaultAsync(cancellationToken);
        double aCageLengthVal = aCageLength.HasValue ? Convert.ToDouble(aCageLength.Value) : 0.0;

        foreach (var a in aLayers)
        {
            var no = a.LayerNo ?? 0;

            // 将 A 层数据复制到对应编号的 B 层
            if (no > 0 && bByLayerNo.TryGetValue(no, out var b))
            {
                b.IsOccupied = a.IsOccupied;
                b.GlassSpec = a.GlassSpec;
                b.Width = a.Width;
                b.RemainingLength = a.RemainingLength;
                b.GlassID = a.GlassID;
            }

            // 无论是否匹配到 B 层，始终清空 A 层并恢复 RemainingLength
            a.IsOccupied = false;
            a.GlassSpec = null;
            a.Width = null;
            a.GlassID = null;
            a.RemainingLength = aCageLengthVal;
        }

        await context.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return moveMaterials.Count;
    }

}

public sealed class ShiftResult
{
    public bool Success { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public int MovedCount { get; private set; }
    public string? ACageCode { get; private set; }
    public string? BCageCode { get; private set; }

    public static ShiftResult Ok(int movedCount, string aCageCode, string bCageCode) =>
        new()
        {
            Success = true,
            Message = "顺移完成",
            MovedCount = movedCount,
            ACageCode = aCageCode,
            BCageCode = bCageCode
        };
    public static ShiftResult Fail(string message) =>
        new()
        {
            Success = false,
            Message = message,
            MovedCount = 0
        };
}
