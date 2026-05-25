using System;
using System.Linq;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;

namespace GlassWarehouseSystem.Repositories;

/// <summary>
/// 物料仓库类。
/// 提供从 MySQL Materials 表中插入和查询物料的数据访问方法。
/// </summary>
public class MaterialRepository
{
    /// <summary>
    /// 创建一条 Pending 状态的物料记录。
    /// 自动生成 GUID 作为 GlassID 主键。
    /// 主要用于测量模式下读取到长宽后的预落库操作。
    /// 
    /// 注意：InboundService 目前直接使用 DbContext 创建物料记录（绕过此方法），
    /// 此方法作为备选的简单写入入口保留。
    /// </summary>
    /// <param name="length">玻璃长度（mm）</param>
    /// <param name="width">玻璃宽度（mm）</param>
    /// <returns>新创建的 Material 实体（已保存到数据库）</returns>
    public Material InsertPendingInbound(decimal length, decimal width)
    {
        using var context = new WarehouseDbContext();

        var material = new Material
        {
            GlassID = Guid.NewGuid().ToString(),
            Length = length,
            Width = width,
            Status = (MaterialStatus)1 // Pending = 待入库
        };

        context.Materials.Add(material);
        context.SaveChanges();
        return material;
    }

    /// <summary>
    /// 根据 GlassID 主键查询物料实体。
    /// </summary>
    /// <param name="glassId">玻璃唯一标识</param>
    /// <returns>物料实体，找不到时返回 null</returns>
    public Material? GetById(string glassId)
    {
        using var context = new WarehouseDbContext();
        return context.Materials.FirstOrDefault(m => m.GlassID == glassId);
    }
}
