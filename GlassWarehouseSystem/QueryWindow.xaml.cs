using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Services;
using GlassWarehouseSystem.ViewModels;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows;

namespace GlassWarehouseSystem
{
    public partial class QueryWindow : Window
    {
        // 缓存键名
        private const string ALL_PLANS_CACHE_KEY = "QueryData:Status0Material";

        public QueryWindow()
        {
            InitializeComponent();
            btnQuery.Click += BtnQuery_Click;
            btnClear.Click += BtnClear_Click;
        }

        private void BtnQuery_Click(object sender, RoutedEventArgs e)
        {
            // 1. 获取输入
            string inputFlowCard = txtFlowCardNo.Text.Trim();
            string inputOrderNo = txtOrderNo.Text.Trim();
            string inputClient = txtClientName.Text.Trim();
            string inputProduct = txtProductName.Text.Trim();

            string inputOrderIndex = txtOrderIndex.Text.Trim(); // 订序
            string inputLength = txtLengthStart.Text.Trim();         // 长边
            string inputWidth = txtWidthStart.Text.Trim();           // 短边

            string startLenStr = txtLengthStart.Text.Trim(); // 假设左边框名是 txtLengthStart
            string endLenStr = txtLengthEnd.Text.Trim();     // 假设右边框名是 txtLengthEnd

            string startWidStr = txtWidthStart.Text.Trim(); // 假设左边框名是 txtLengthStart
            string endWidStr = txtWidthEnd.Text.Trim();     // 假设右边框名是 txtLengthEnd


            float? searchStartLen = float.TryParse(startLenStr, out float sL) ? sL : (float?)null;
            float? searchEndLen = float.TryParse(endLenStr, out float eL) ? eL : (float?)null;


            float? searchStartWid = float.TryParse(startWidStr, out float sW) ? sW : (float?)null;
            float? searchEndWid = float.TryParse(endWidStr, out float eW) ? eW : (float?)null;

            string inputGlassID = txtGlassID.Text.Trim();       // 小片ID
            DateTime? startDate = dtStart.SelectedDate?.Date;   // 开始日期
            DateTime? endDate = dtEnd.SelectedDate?.Date;       // 结束日期

            try
            {
                // 【关键改动】：既然是手动点击查询，我们就将 tryRedisFirst 设为 false
                // 这样 CacheQueryService 会跳过读 Redis，直接执行下面的 dbQueryFunc (扫数据库)
                // 但查完后，它依然会把结果写回 Redis，方便后续其他可能的调用（如导出、详情查看等）

                var allPlans = CacheQueryService.GetCachedData<QueryMaterialViewModel>(
                    ALL_PLANS_CACHE_KEY,
                    () =>
                    {
                        using (var db = new WarehouseDbContext())
                        {
                            // 严格执行你的要求：materials(status=0) + orders(status=0/1)
                            // 定序：orders.ID
                            return (from m in db.Materials.AsNoTracking()
                                    join o in db.Orders.AsNoTracking() on m.OrderID equals o.OrderID
                                    where m.Status == 0 
                                    //&& (o.Status == 0 || o.Status == 1)
                                    orderby o.Id ascending
                                    select new QueryMaterialViewModel
                                    {
                                        ID = m.GlassID,               // 对应数据库 GlassID
                                        Name = m.ProductName,         // 对应数据库 ProductName
                                        Length = (float)(m.Length),
                                        Width = (float)(m.Width),
                                        Thickness = (float)(m.Thickness),
                                        ClientName = o.CustomerName,  // 对应数据库 CustomerName
                                        OrderCard = o.OrderNo,        // 对应数据库 OrderNo
                                        FlowCardNo = o.FlowCardNo,    // 对应数据库 FlowCardNo
                                        SequenceID = o.Id,
                                        // 以下为 UI 补充字段，数据库若无则填默认值
                                        ProductGroup = "数据库无字段",
                                        ModelNo = "数据库无字段",
                                        GroupName = "数据库无字段",
                                        Quantity = 0,
                                        InboundTime = m.InboundTime ?? DateTime.Now
                                    }).ToList();
                        }
                    },
                    TimeSpan.FromMinutes(10),
                    tryRedisFirst: false // <--- 强制不看缓存，直接扫库，保证数据的绝对实时
                );

                // 2. 内存筛选 (根据用户输入的四个条件)
                IEnumerable<QueryMaterialViewModel> filtered = allPlans;

                if (!string.IsNullOrWhiteSpace(inputFlowCard))
                    filtered = filtered.Where(x => x.FlowCardNo != null && x.FlowCardNo.Contains(inputFlowCard, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(inputOrderNo))
                    filtered = filtered.Where(x => x.OrderCard != null && x.OrderCard.Contains(inputOrderNo, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(inputClient))
                    filtered = filtered.Where(x => x.ClientName != null && x.ClientName.Contains(inputClient, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(inputProduct))
                    filtered = filtered.Where(x => x.Name != null && x.Name.Contains(inputProduct, StringComparison.OrdinalIgnoreCase));

                // --- 新增五个 ---

                // 1. 小片ID (对应数据库的 GlassID/ID)
                if (!string.IsNullOrWhiteSpace(inputGlassID))
                    filtered = filtered.Where(x => x.ID != null && x.ID.Contains(inputGlassID, StringComparison.OrdinalIgnoreCase));

                // 订序搜索 (搜索我们在上面存的 o.ID)
                if (!string.IsNullOrWhiteSpace(inputOrderIndex))
                    filtered = filtered.Where(x => x.SequenceID != null && x.SequenceID.ToString().Contains(inputOrderIndex));

                // 数值模糊搜索
                // 长边
                if (searchStartLen.HasValue) filtered = filtered.Where(x => x.Length >= searchStartLen.Value);
                if (searchEndLen.HasValue) filtered = filtered.Where(x => x.Length <= searchEndLen.Value);

                // 短边 (注意：一定是 x.Width)
                if (searchStartWid.HasValue) filtered = filtered.Where(x => x.Width >= searchStartWid.Value);
                if (searchEndWid.HasValue) filtered = filtered.Where(x => x.Width <= searchEndWid.Value);

                // 时间筛选 (修正：只有当时间不是今天，或者你确定要按时间查时再启用)
                // 建议：如果用户没改过 DatePicker，就不进这个过滤
                if (dtStart.SelectedDate.HasValue && dtStart.SelectedDate.Value.Date != DateTime.Now.Date)
                {
                    filtered = filtered.Where(x => x.InboundTime.Date >= startDate.Value);
                }
                if (dtEnd.SelectedDate.HasValue && dtEnd.SelectedDate.Value.Date != DateTime.Now.Date)
                {
                    filtered = filtered.Where(x => x.InboundTime.Date <= endDate.Value);
                }


                // 3. 渲染 UI
                var finalResult = filtered.Select((item, index) => { item.RowNo = index + 1; return item; }).ToList();
                dgMaterial.ItemsSource = finalResult;

            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库拉取失败: {ex.Message}");
            }
        }
        

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            txtFlowCardNo.Clear();
            txtOrderNo.Clear();
            txtClientName.Clear();
            txtProductName.Clear();

            txtOrderIndex.Clear(); // 清理订序
            txtLengthStart.Clear();     // 清理长边
            txtLengthEnd.Clear();
            txtWidthStart.Clear();      // 清理短边
            txtWidthEnd.Clear();
            txtGlassID.Clear();    // 清理小片ID

            dtStart.SelectedDate = DateTime.Now;
            dtEnd.SelectedDate = DateTime.Now;

            dgMaterial.ItemsSource = null;

            // 清理界面时同时清理该业务缓存
            RedisHelper.Db.KeyDelete(ALL_PLANS_CACHE_KEY);
        }
    }
}