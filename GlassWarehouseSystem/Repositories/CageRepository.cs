using System;
using System.Collections.Generic;
using System.Linq;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;
using GlassWarehouseSystem.Services;
using Microsoft.EntityFrameworkCore;

namespace GlassWarehouseSystem.Repositories;

/// <summary>
/// 笼子仓库类。
/// 提供从 MySQL Cages 表中查询笼子的数据访问方法。
/// 主要被 CageFinder（笼位分配算法）调用，获取所有在线笼及其层信息。
/// </summary>
public class CageRepository
{
    /// <summary>
    /// 获取所有在线笼子及其层数据。
    /// 使用EFCore的Include，一次性将 Cage + Layers 数据加载到内存。
    /// 筛选条件：IsOnline 为 true 或 null（null 视为在线），排除已下线的笼子。
    /// 结果按 CageCode 排序，确保分配算法的确定性。
    /// </summary>
    /// <returns>在线笼子列表（含层数据）</returns>
    public List<Cage> GetOnlineCagesWithLayers()
    {
        return CacheQueryService.GetCachedData<Cage>(
            "cages:online_with_layers",
            () =>
            {
                using var context = new WarehouseDbContext();
                return context.Cages
                    .Include(c => c.Layers)
                    .Where(c => c.IsOnline == null || c.IsOnline == true)
                    .OrderBy(c => c.CageCode)
                    .ToList();
            },
            TimeSpan.FromMinutes(5)
        );
    }

    /// <summary>
    /// 使缓存失效，下次调用 GetOnlineCagesWithLayers 时重新从数据库读取。
    /// </summary>
    public static void InvalidateCache()
    {
        CacheQueryService.ClearCacheByKeys("cages:online_with_layers");
    }
}
