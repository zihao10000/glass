using System.Linq;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;

namespace GlassWarehouseSystem.Repositories;

/// <summary>
/// 层仓库类。
/// 提供从 MySQL Layers 表中查询层信息的数据访问方法。
/// 主要被 InboundService 在计算槽位号时调用。
/// </summary>
public class LayerRepository
{
    /// <summary>
    /// 获取指定笼子指定层中已入库的物料数量（即已占用的槽位数）。
    /// 用于计算新入库玻璃的 SlotNo = GetUsedSlotCount() + 1。
    /// </summary>
    /// <param name="cageCode">笼子编码（如 "A01"）</param>
    /// <param name="layerNo">层号（1~N）</param>
    /// <returns>已入库物料数量</returns>
    public int GetUsedSlotCount(string cageCode, int layerNo)
    {
        using var context = new WarehouseDbContext();
        return context.Materials.Count(m => m.CurrentCage == cageCode && m.CurrentLayer == layerNo && m.InboundTime != null);
    }

    /// <summary>
    /// 根据LayerID主键查询层实体。
    /// </summary>
    /// <param name="layerId">层主键</param>
    /// <returns>层实体，找不到时返回 null</returns>
    public Layer? GetById(string layerId)
    {
        using var context = new WarehouseDbContext();
        return context.Layers.FirstOrDefault(x => x.LayerID == layerId);
    }
}
