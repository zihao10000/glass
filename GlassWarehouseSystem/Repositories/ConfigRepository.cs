using System.Collections.Generic;
using System.Linq;
using GlassWarehouseSystem.Data;

namespace GlassWarehouseSystem.Repositories;

/// <summary>
/// 【数据访问层】配置仓库类。
/// 提供从 MySQL config 表取所有配置参数的方法。
/// 返回以 KeyName 字段为 Key、Value 字段为 Value 的字典。
/// 
/// 主要被 UI 配置界面调用，用于展示和编辑当前系统参数。
/// 运行时的参数读取则由 AppConfig 的内存缓存直接提供，不经过此仓库。
/// </summary>
public class ConfigRepository
{
    /// <summary>
    /// 获取所有配置参数（键值对字典）。
    /// 先将所有 ConfigRow 实体加载到内存，再过滤掉 KeyName 为空的行，
    /// 最后构建 KeyName → Value 的字典返回。
    /// </summary>
    /// <returns>参数名 → 参数值的字典</returns>
    public Dictionary<string, string> GetAllByDescribe()
    {
        using var context = new WarehouseDbContext();
        return context.ConfigRows
            .ToList() // 先加载到内存（因为 HasNoKey 实体不支持复杂 LINQ 翻译）
            .Where(x => !string.IsNullOrWhiteSpace(x.KeyName))
            .ToDictionary(x => x.KeyName!, x => x.Value ?? string.Empty);
    }
}
