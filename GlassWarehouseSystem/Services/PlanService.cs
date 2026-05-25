using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace GlassWarehouseSystem.Services
{
    /// <summary>
    /// 【出库辅助】当前计划大盘查询服务。
    /// 仅服务于获取业务中还处于"在库内"(Status = InStock)的所有有效库存商品物料数据清单。
    /// 这些都是在接下来时刻待排队等待发配进入出库排产序列或者要呈现在入库统计面板的数据。
    /// 
    /// 提供根据特定搜索关键字做模糊过滤的能力。
    /// </summary>
    public class PlanService
    {
        /// <summary>
        /// 列表检索获取全厂库存。
        /// 用于将查出结果绑定给 InboundWindow 里面的展示主 DataGrid 清单。
        /// 并自动在关联的内联映射里一并加载 Order 表以便得到该批库存的归属客户和流传号。
        /// </summary>
        /// <param name="clientFilter">基于文本搜索客户名。如指定，则对关联的 order 客户进行相似度匹配</param>
        /// <param name="flowCardFilter">基于生产派工追踪批卡的扫卡单号前部分相似匹配过滤条件</param>
        /// <param name="take">保险与性能防护：最多一管次往界面拉出的条目总量限制上限（默认取最新的100件）保障流畅 UI 大表渲染</param>
        /// <returns>内存封装后的所有匹配玻璃明细实体集合</returns>
        public async Task<List<Material>> GetInStockMaterialsAsync(
            string clientFilter = null,
            string flowCardFilter = null,
            int take = 100)
        {
            using var context = new WarehouseDbContext();

            // 主搜索骨架构建 -- 左连接装载关联的生产单子,而且永远卡死范围查寻是仅仅限：在库状态(Status==1)的！
            var query = context.Materials
                .Include(m => m.Order)
                .Where(m => m.Status == MaterialStatus.InStock);  // 核心：只取 status=1（在库）

            // 构建 EF Core 的扩展 Where 过滤条件表达式树。
            if (!string.IsNullOrWhiteSpace(clientFilter))
            {
                query = query.Where(m => m.Order != null && m.Order.CustomerName.Contains(clientFilter.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(flowCardFilter))
            {
                query = query.Where(m => m.Order != null && m.Order.FlowCardNo.Contains(flowCardFilter.Trim()));
            }

            // 执行下推到 MySQL 的原生复杂多级复合排序。
            // 排序规则（与原 InboundWindow 中的习惯保持一致）：
            // 依次比较顺序： 1. 按客户名称拼音/字典升序 
            //               2. 遭遇同个定主时，拿短边Width按从宽降序（大片先推展现列前去） 
            //               3. 若连长宽都一致同源则遵循先进先出（按入库打卡时间戳先后）
            var materials = await query
                .OrderBy(m => m.Order != null ? m.Order.CustomerName : "")
                .ThenByDescending(m => m.Width)
                .ThenBy(m => m.InboundTime)
                .Take(take)  // 强制截流只拉约定个数保系统反应能力
                .ToListAsync();

            return materials;
        }
    }
}
