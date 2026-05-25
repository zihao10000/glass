using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GlassWarehouseSystem.Config;
using GlassWarehouseSystem.Models;
using GlassWarehouseSystem.Repositories;

namespace GlassWarehouseSystem.Services;

public class CageFinder
{
    private readonly CageRepository _cageRepository;

    public CageFinder(CageRepository cageRepository)
    {
        _cageRepository = cageRepository;
    }

    /// <summary>
    /// 寻找最合适的入库笼层，两步策略：
    ///   第一步 — 在所有 A 笼中查找规格相同（GlassSpec 匹配）且剩余空间足够的层。
    ///   第二步 — 若第一步无结果，取第一个完全空闲层（GlassSpec 为空）且剩余空闲。
    ///            RemainingLength 为 null 表示该层从未使用过，视为容量充足。不检查笼子物理尺寸。
    /// 两步均无结果时返回 null 并通过 reason 输出原因。
    /// </summary>
    /// <param name="glassLength">玻璃长度（数据库精确值，单位 mm）</param>
    /// <param name="glassWidth">玻璃宽度（数据库精确值，单位 mm）</param>
    /// <param name="reason">未找到可用层时的原因说明</param>
    /// <returns>找到时返回 FindCageResult，否则返回 null</returns>
    public FindCageResult? FindCage(decimal glassLength, decimal glassWidth, out string reason)
    {
        reason = string.Empty;

        var glassSpacing = Convert.ToDecimal(AppConfig.GetFloat("GlassSpacing"));
        var requiredLength = glassLength + glassSpacing;
        var glassSpec = BuildSpec(glassLength, glassWidth);

        var cages = _cageRepository.GetOnlineCagesWithLayers();

        // 只考虑 A 笼（入库通道）
        var aCages = cages
            .Where(c => IsInboundCage(c.LocationType))
            .OrderBy(c => c.CageCode)
            .ToList();

        if (aCages.Count == 0)
        {
            reason = "未找到在线 A 笼，无法入库。";
            return null;
        }

        // ── 第一步：找规格相同且剩余空间足够的层（同规格集中存放）──
        foreach (var cage in aCages)
        {
            foreach (var layer in cage.Layers.OrderBy(l => l.LayerNo))
            {
                if ((layer.LayerNo ?? 0) <= 0) continue;
                if ((decimal)(layer.RemainingLength ?? 0.0) < requiredLength) continue;

                if (string.Equals(layer.GlassSpec, glassSpec, StringComparison.OrdinalIgnoreCase))
                    return BuildResult(cage, layer);
            }
        }

        // ── 第二步：无同规格层，取第一个空闲层（GlassSpec为空）且容量足够─────
        // RemainingLength 为 null 表示该层从未使用，视为容量充足；不再检查笼子物理尺寸
        foreach (var cage in aCages)
        {
            var emptyLayer = cage.Layers
                .OrderBy(l => l.LayerNo)
                .FirstOrDefault(l =>
                    (l.LayerNo ?? 0) > 0
                    && string.IsNullOrWhiteSpace(l.GlassSpec)
                    && (l.RemainingLength == null || (decimal)l.RemainingLength.Value >= requiredLength));

            if (emptyLayer != null)
                return BuildResult(cage, emptyLayer);
        }

        // 两步均无结果：所有层均已被其他规格占用
        var totalLayerCount = aCages.Sum(c => c.Layers.Count(l => (l.LayerNo ?? 0) > 0));
        var occupiedCount   = aCages.Sum(c => c.Layers.Count(l =>
            (l.LayerNo ?? 0) > 0 && !string.IsNullOrWhiteSpace(l.GlassSpec)));

        reason = $"无可用笼层：共 {totalLayerCount} 层均已占用，无同规格层也无空闲层。玻璃 L={glassLength} W={glassWidth}。";

        return null;
    }

    /// <summary>
    /// 根据笼和层信息构建 FindCageResult 并计算 PLC 目标坐标。
    /// </summary>
    private static FindCageResult BuildResult(Cage cage, Layer layer)
    {
        // 优先使用 Layer.Coord（DB 预存坐标），与 TryMapPlcPositionToLayer 保持一致
        int targetPosValue;
        if (layer.Coordinate.HasValue)
        {
            targetPosValue = Convert.ToInt32(layer.Coordinate.Value);
        }
        else
        {
            var gridStart = cage.GridStartCoord ?? 0m;
            var space     = (decimal)(layer.Space ?? (double?)cage.InboundGap ?? 0.0);
            targetPosValue = Convert.ToInt32(gridStart + ((layer.LayerNo ?? 1) - 1) * space);
        }

        return new FindCageResult
        {
            CageCode      = cage.CageCode,
            LayerNo       = layer.LayerNo ?? 0,
            LayerID       = layer.LayerID,
            TargetPosValue = targetPosValue,
            LocationType  = cage.LocationType
        };
    }

    /// <summary>
    /// 向后兼容的重载，调用方不需要 reason 时使用。
    /// </summary>
    public FindCageResult? FindCage(decimal glassLength, decimal glassWidth)
        => FindCage(glassLength, glassWidth, out _);

    /// <summary>
    /// 检查是否存在任何在线的 A 笼（入库通道笼）。
    /// </summary>
    public bool HasOnlineACage()
    {
        var cages = _cageRepository.GetOnlineCagesWithLayers();
        return cages.Any(c => IsInboundCage(c.LocationType));
    }

    /// <summary>
    /// 检查玻璃尺寸是否超过所有在线 A 笼的最大容纳尺寸（长和宽均须满足）。
    /// 返回 true 表示超限，false 表示至少有一个 A 笼能容纳。
    /// </summary>
    public bool IsGlassOversizeForAllACages(decimal glassLength, decimal glassWidth, out string message)
    {
        message = string.Empty;
        var aCages = _cageRepository.GetOnlineCagesWithLayers()
            .Where(c => IsInboundCage(c.LocationType))
            .ToList();

        if (aCages.Count == 0)
        {
            message = "未找到在线A笼。";
            return true;
        }

        // 只要有一个 A 笼的长和宽均能容纳玻璃，就认为不超限
        var hasCapableCage = aCages.Any(c =>
            (c.Length ?? 0m) >= glassLength &&
            (c.Width ?? 0m) >= glassWidth);

        if (hasCapableCage)
        {
            return false;
        }

        var maxLength = aCages.Max(c => c.Length ?? 0m);
        var maxWidth = aCages.Max(c => c.Width ?? 0m);
        message = $"玻璃尺寸超限: L={glassLength}, W={glassWidth}，A笼最大可用尺寸 L={maxLength}, W={maxWidth}";
        return true;
    }

    /// <summary>
    /// 将长宽拼成规格字符串，用于同规格层的匹配比较。
    /// </summary>
    private static string BuildSpec(decimal length, decimal width)
        => string.Create(CultureInfo.InvariantCulture, $"{length}×{width}");

    /// <summary>
    /// 判断笼子是否为 A 笼（入库通道），LocationType 以 "A" 开头即视为 A 笼。
    /// </summary>
    private static bool IsInboundCage(string? locationType)
        => !string.IsNullOrWhiteSpace(locationType)
           && locationType.StartsWith("A", StringComparison.OrdinalIgnoreCase);
}
