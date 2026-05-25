using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using GlassWarehouseSystem.Config;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;
using GlassWarehouseSystem.Repositories;
using GlassWarehouseSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace GlassWarehouseSystem
{
    /// <summary>
    /// 出笼主界面。
    /// 排序由 config 表中 OutSortField / OutSortDir 决定。
    /// 出笼完成后从 Materials 删除、写入 HistoryMaterials 归档。
    /// </summary>
    public partial class OutboundWindow : Window
    {
        private readonly ObservableCollection<OutboundTaskItemViewModel> _taskItems = new();
        private readonly ObservableCollection<OutboundQueueViewModel> _queueItems = new();
        private readonly ObservableCollection<OutboundHistoryViewModel> _historyItems = new();
        private readonly ObservableCollection<OrderFilterItem> _orderFilterItems = new();

        private readonly OutboundService _outboundService = new();
        private readonly PlcService _plcService = new(new PlcClient());
        private readonly LogRepository _logRepository = new();

        private bool _isOutboundRunning;
        /// <summary>全量任务列表，用于订单筛选后恢复显示。</summary>
        private List<OutboundTaskItemViewModel> _allTaskItems = new();

        public OutboundWindow()
        {
            InitializeComponent();

            dgOutboundTask.ItemsSource = _taskItems;
            dgOutboundQueue.ItemsSource = _queueItems;
            dgOutboundHistory.ItemsSource = _historyItems;

            InitializeDatabase();
            UpdateServiceButtons();
            UpdateParamDisplay();

            _outboundService.OnLogActivity += msg => DispatchToLog("TRACE", msg);
            _outboundService.OnOutboundItemCompleted += OnOutboundItemCompleted;
            _outboundService.OnPrintTrigger += OnPrintTriggerReceived;

            AppendLog("INFO", "出笼系统启动完成，等待操作...");
        }

        private void InitializeDatabase()
        {
            try
            {
                using var context = new WarehouseDbContext();
                context.Database.EnsureCreated();
                AppendLog("INFO", "数据库连接成功");
            }
            catch (Exception ex)
            {
                AppendLog("ERROR", $"数据库初始化失败: {ex.Message}");
            }
        }

        #region 加载在库物料

        /// <summary>
        /// 从数据库加载所有在库物料，排序由 config 表配置。
        /// </summary>
        private async void BtnLoadMaterials_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("INFO", "正在加载B笼在库物料...");

            try
            {
                var materials = await _outboundService.LoadInStockMaterialsSortedAsync();
                if (materials.Count == 0)
                {
                    AppendLog("WARNING", "B笼内没有在库物料");
                    MessageBox.Show("当前B笼内没有在库物料", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                LoadTaskItems(materials);
                BuildQueueSummary();
                UpdateStatistics();
                UpdateParamDisplay();

                string sortField = AppConfig.GetStringOrDefault("OutSortField", "Length");
                string sortDir = AppConfig.GetStringOrDefault("OutSortDir", "Desc");
                AppendLog("SUCCESS", $"加载完成，共 {materials.Count} 件B笼物料" +
                    $"（排序: {(sortField.Equals("Length", StringComparison.OrdinalIgnoreCase) ? "长边" : "短边")} " +
                    $"{(sortDir.Equals("Desc", StringComparison.OrdinalIgnoreCase) ? "从大到小" : "从小到大")}）");
            }
            catch (Exception ex)
            {
                AppendLog("ERROR", $"加载物料失败: {ex.Message}");
                MessageBox.Show($"加载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            BtnLoadMaterials_Click(sender, e);
        }

        /// <summary>
        /// 计算 PLC 目标笼位值。优先使用 Layer.Coord（DB 预存坐标），
        /// 回退至 GridStartCoord + (LayerNo-1) * Space 公式计算。
        /// 与 CageFinder.BuildResult、TryMapPlcPositionToLayer 保持一致。
        /// </summary>
        private static float CalcTargetPos(Material m, Dictionary<string, Cage> cageDict,
            Dictionary<(string, int), Layer> layerDict)
        {
            if (string.IsNullOrEmpty(m.CurrentCage) || m.CurrentLayer == null) return 0f;
            if (!cageDict.TryGetValue(m.CurrentCage, out var cage)) return 0f;

            int layerNo = m.CurrentLayer ?? 1;

            // 优先使用 Layer.Coord
            if (layerDict.TryGetValue((m.CurrentCage, layerNo), out var layer) && layer.Coordinate.HasValue)
                return (float)layer.Coordinate.Value;

            decimal start = cage.GridStartCoord ?? 0m;
            decimal space = layer?.Space != null
                ? (decimal)layer.Space.Value
                : cage.Space ?? 0m;
            return (float)(start + (layerNo - 1) * space);
        }

        private void LoadTaskItems(List<Material> materials)
        {
            // 一次性预加载所有 Cage 和 Layer
            Dictionary<string, Cage> cageDict;
            Dictionary<(string, int), Layer> layerDict;
            using (var context = new WarehouseDbContext())
            {
                cageDict = context.Cages.AsNoTracking()
                    .ToDictionary(c => c.CageCode, c => c);
                layerDict = context.Layers.AsNoTracking()
                    .Where(l => l.CageID != null && l.LayerNo != null)
                    .ToDictionary(l => (l.CageID!, l.LayerNo!.Value), l => l);
            }

            _allTaskItems.Clear();
            int seq = 1;
            foreach (var m in materials)
            {
                float targetPos = CalcTargetPos(m, cageDict, layerDict);
                _allTaskItems.Add(new OutboundTaskItemViewModel
                {
                    SequenceNo = seq++,
                    FlowCardNo = m.Order?.FlowCardNo ?? "",
                    Length = m.Length,
                    Width = m.Width,
                    ID = m.GlassID,
                    CageCode = m.CurrentCage ?? "-",
                    LayerNo = m.CurrentLayer?.ToString() ?? "-",
                    TargetPos = targetPos,
                    Status = "等待",
                    OrderName = m.OrderName ?? "-",
                    ClientName = m.Order?.CustomerName ?? "-",
                    MaterialID = m.GlassID,
                    Material = m,
                    GroupID = m.GroupID
                });
            }

            // 同步到可观察集合
            _taskItems.Clear();
            foreach (var item in _allTaskItems) _taskItems.Add(item);

            // 填充订单多选列表：同时携带订单名以便于识别
            _orderFilterItems.Clear();
            var orderGroups = _allTaskItems
                .Where(t => !string.IsNullOrEmpty(t.Material?.OrderID))
                .GroupBy(t => t.Material!.OrderID!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new OrderFilterItem
                {
                    OrderId   = g.Key,
                    OrderName = g.Select(x => x.Material?.OrderName)
                                 .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "",
                    IsChecked = false
                })
                .OrderBy(x => x.OrderName)
                .ThenBy(x => x.OrderId);
            foreach (var item in orderGroups)
                _orderFilterItems.Add(item);
            lstOrders.ItemsSource = _orderFilterItems;
            chkSelectAll.IsChecked = false;
            UpdateOrderDropdownLabel();
        }

        /// <summary>
        /// 按笼号+层号汇总出笼队列，显示在右侧面板。
        /// </summary>
        private void BuildQueueSummary()
        {
            _queueItems.Clear();
            var groups = _taskItems
                .Where(t => t.Status == "等待" || t.Status == "执行中")
                .GroupBy(t => new { t.CageCode, t.LayerNo })
                .OrderBy(g => g.Key.CageCode).ThenBy(g => g.Key.LayerNo);

            foreach (var g in groups)
            {
                _queueItems.Add(new OutboundQueueViewModel
                {
                    CageCode = g.Key.CageCode,
                    LayerNo = g.Key.LayerNo,
                    Count = g.Count(),
                    TargetPos = g.First().TargetPos,
                    Status = "等待"
                });
            }
        }

        #endregion

        #region 订单筛选

        private void BtnOrderDropdown_Click(object sender, RoutedEventArgs e)
        {
            popupOrders.IsOpen = !popupOrders.IsOpen;
        }

        private void ChkSelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool check = chkSelectAll.IsChecked == true;
            foreach (var item in _orderFilterItems)
                item.IsChecked = check;
            lstOrders.Items.Refresh();
        }

        private void OrderCheckBox_Click(object sender, RoutedEventArgs e)
        {
            int checkedCount = _orderFilterItems.Count(x => x.IsChecked);
            if (checkedCount == 0)
                chkSelectAll.IsChecked = false;
            else if (checkedCount == _orderFilterItems.Count)
                chkSelectAll.IsChecked = true;
            else
                chkSelectAll.IsChecked = null;
        }

        private void BtnFilterByOrder_Click(object sender, RoutedEventArgs e)
        {
            popupOrders.IsOpen = false;
            var checkedIds = _orderFilterItems.Where(x => x.IsChecked).Select(x => x.OrderId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (checkedIds.Count == 0)
            {
                BtnShowAll_Click(sender, e);
                return;
            }

            _taskItems.Clear();
            int seq = 1;
            foreach (var item in _allTaskItems.Where(t => !string.IsNullOrEmpty(t.Material?.OrderID) && checkedIds.Contains(t.Material!.OrderID!)))
            {
                item.SequenceNo = seq++;
                _taskItems.Add(item);
            }

            UpdateOrderDropdownLabel();
            dgOutboundTask.Items.Refresh();
            UpdateStatistics();
            BuildQueueSummary();
            AppendLog("INFO", $"已筛选 {checkedIds.Count} 个订单，共 {_taskItems.Count} 件");
        }

        private void BtnShowAll_Click(object sender, RoutedEventArgs e)
        {
            popupOrders.IsOpen = false;
            foreach (var item in _orderFilterItems)
                item.IsChecked = false;
            chkSelectAll.IsChecked = false;
            lstOrders.Items.Refresh();

            _taskItems.Clear();
            int seq = 1;
            foreach (var item in _allTaskItems)
            {
                item.SequenceNo = seq++;
                _taskItems.Add(item);
            }

            UpdateOrderDropdownLabel();
            dgOutboundTask.Items.Refresh();
            UpdateStatistics();
            BuildQueueSummary();
            AppendLog("INFO", $"已恢复全部显示，共 {_taskItems.Count} 件");
        }

        private void UpdateOrderDropdownLabel()
        {
            var checkedCount = _orderFilterItems.Count(x => x.IsChecked);
            txtOrderDropdownLabel.Text = checkedCount == 0
                ? "(全部)"
                : $"已选 {checkedCount} 个订单";
        }

        #endregion

        #region 表格选择

        private void DgOutboundTask_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgOutboundTask.SelectedItem is OutboundTaskItemViewModel selected)
                UpdateDetailPanel(selected);
        }


        private void UpdateDetailPanel(OutboundTaskItemViewModel item)
        {
            txtDetailTitle.Text = $"详细信息 ({item.ID}){(item.IsGrouped ? " 🔗连组" : "")}";
            txtDetailID.Text = $"GlassID: {item.ID}";
            txtDetailFlow.Text = $"流程卡: {item.FlowCardNo}";
            txtDetailCage.Text = $"位置: {item.CageCode}-{item.LayerNo}层 (笼位{item.TargetPos:F1})";
            txtDetailOrder.Text = $"订单: {item.Material?.Order?.OrderNo ?? "N/A"}";
            txtDetailSize.Text = $"长: {item.Length:F3}   宽: {item.Width:F3}   厚: {item.Material?.Thickness ?? 0:F4}";
            txtDetailStatus.Text = item.IsGrouped
                ? $"状态: {item.Status}  |  连组ID: {item.GroupID}"
                : $"状态: {item.Status}";
        }

        private async void BtnManualOutbound_Click(object sender, RoutedEventArgs e)
        {
            if (_isOutboundRunning)
            {
                MessageBox.Show("自动出笼正在运行中，请先停止后再使用手动出笼。", "禁止操作", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selected = dgOutboundTask.SelectedItem as OutboundTaskItemViewModel;
            if (selected == null) { MessageBox.Show("请先选择要出笼的物料", "提示"); return; }
            if (selected.Status != "等待") { MessageBox.Show("该物料状态不可出笼", "提示"); return; }

            // 查找同组所有等待出笼的成员（包含自身）
            var groupItems = selected.IsGrouped
                ? _taskItems.Where(t => t.GroupID == selected.GroupID && t.Status == "等待").ToList()
                : new System.Collections.Generic.List<OutboundTaskItemViewModel> { selected };

            string confirmMsg = groupItems.Count > 1
                ? $"选中物料属于连组（共 {groupItems.Count} 片）：\n" +
                  string.Join("\n", groupItems.Select(t => $"  • {t.ID}  {t.CageCode}-{t.LayerNo}层")) +
                  "\n\n以上所有片将一并出笼并归档，确认继续？"
                : $"确认手动出笼: {selected.ID}？\n读取 PLC 当前位，计算移动距离，执行出笼流程后归档。";

            if (MessageBox.Show(confirmMsg, "确认手动出笼",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            int successCount = 0;
            // 第一片走完整 PLC 流程（ManualOutboundAsync 内部会触发打印）；
            // 连组其余成员物理上已随第一片取出，直接归档并单独触发打印
            bool isFirst = true;
            foreach (var item in groupItems)
            {
                bool ok;
                if (isFirst)
                {
                    AppendLog("INFO", $"手动出笼(PLC): {item.ID}  笼位={item.TargetPos:F1}");
                    ok = await _outboundService.ManualOutboundAsync(_plcService, item.MaterialID, item.TargetPos);
                    isFirst = false;
                }
                else
                {
                    AppendLog("INFO", $"手动出笼(连组归档): {item.ID}");
                    // 归档前缓存物料（归档后会从 Materials 表删除）
                    var matForPrint = item.Material;
                    ok = await _outboundService.ProcessOutboundAsync(item.MaterialID);
                    // 连组非首片也需要打印标签
                    if (ok && matForPrint != null)
                        OnPrintTriggerReceived(matForPrint);
                }

                if (ok)
                {
                    item.Status = "完成";
                    AddHistory(item.ID, 0f, groupItems.Count > 1 ? "手动连组出笼" : "手动出笼成功");
                    AppendLog("SUCCESS", $"出笼成功并归档: {item.ID}");
                    successCount++;
                }
                else
                {
                    AppendLog("ERROR", $"出笼失败: {item.ID}");
                }
            }

            dgOutboundTask.Items.Refresh();
            UpdateStatistics();
            BuildQueueSummary();

            if (successCount < groupItems.Count)
                MessageBox.Show($"部分出笼失败（成功 {successCount}/{groupItems.Count}）",
                    "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void BtnResetPlc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _plcService.WriteBool("Addr_Out_StartCmd", false);
                _plcService.WriteBool("Addr_Out_PopCmd", false);
                AppendLog("INFO", "PLC 出笼指令位已全部复位");
            }
            catch (Exception ex)
            {
                AppendLog("ERROR", $"PLC 复位失败: {ex.Message}");
                MessageBox.Show($"PLC 复位失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 导出功能

        private void BtnExportOutbound_Click(object sender, RoutedEventArgs e)
        {
            if (_taskItems.Count == 0)
            {
                MessageBox.Show("无可导出的出笼数据", "提示");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV|*.csv",
                FileName = $"outbound_record_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("序号,GlassID,流程卡,长边,短边,笼号,层号,笼位,状态,客户");
            foreach (var t in _taskItems)
                sb.AppendLine($"{t.SequenceNo},{t.ID},{t.FlowCardNo},{t.Length:F3},{t.Width:F3},{t.CageCode},{t.LayerNo},{t.TargetPos:F1},{t.Status},{t.ClientName}");

            System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            AppendLog("SUCCESS", $"出笼记录已导出: {dlg.FileName}");
        }

        #endregion

        #region 服务控制

        private void BtnStartOutbound_Click(object sender, RoutedEventArgs e)
        {
            if (_taskItems.Count == 0)
            {
                MessageBox.Show("请先加载在库物料", "提示");
                return;
            }

            var pendingItems = _taskItems.Where(t => t.Status == "等待").ToList();
            if (pendingItems.Count == 0)
            {
                MessageBox.Show("无待出笼物料", "提示");
                return;
            }

            if (MessageBox.Show($"即将启动自动出笼，共 {pendingItems.Count} 件。\n出笼后物料将从物料表删除并归档到历史表。\n确认启动？",
                "确认启动", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            _isOutboundRunning = true;
            UpdateServiceButtons();

            _outboundService.StartOutboundLoop(_plcService, pendingItems);
            AppendLog("SUCCESS", $"=== 出笼服务已启动，待处理 {pendingItems.Count} 件 ===");
        }

        private void BtnStopOutbound_Click(object sender, RoutedEventArgs e)
        {
            StopOutbound();
            AppendLog("WARNING", "=== 出笼服务已停止 ===");
        }

        private void StopOutbound()
        {
            _isOutboundRunning = false;
            _outboundService.Stop();
            UpdateServiceButtons();
        }

        private void UpdateServiceButtons()
        {
            btnStartOutbound.IsEnabled = !_isOutboundRunning;
            btnStopOutbound.IsEnabled = _isOutboundRunning;
        }

        /// <summary>
        /// OutboundService 每完成一项出笼后的回调。
        /// </summary>
        private void OnOutboundItemCompleted(string materialId, bool success, float moveDistance)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var item = _taskItems.FirstOrDefault(t => t.MaterialID == materialId);
                if (item != null)
                {
                    item.Status = success ? "完成" : "失败";
                    dgOutboundTask.Items.Refresh();
                    AddHistory(item.ID, moveDistance, success ? "出笼成功" : "出笼失败");
                }
                UpdateStatistics();
                BuildQueueSummary();

                // 全部完成后自动停止
                if (!_taskItems.Any(t => t.Status == "等待"))
                {
                    StopOutbound();
                    AppendLog("SUCCESS", "=== 全部出笼完成 ===");
                }
            });
        }

        /// <summary>
        /// 出笼归档后打印回调。物料对象由 OutboundService 在归档前缓存并直接传入。
        /// 直接以打印模板 + 实际物料数据静默送默认打印机，不弹任何窗口。
        /// </summary>
        private void OnPrintTriggerReceived(Material material)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    AppendLog("INFO", $"[打印] 启动打印任务: {material.GlassID}");
                    PrintWindowDraggable.PrintLabelSilently(
                        material,
                        msg => AppendLog("INFO", $"[打印] {msg}"));
                }
                catch (Exception ex)
                {
                    AppendLog("ERROR", $"[打印] 启动打印失败: {ex.Message}");
                }
            });
        }

        #endregion

        #region 菜单

        private void BtnSwitchToInbound_Click(object sender, RoutedEventArgs e)
        {
            if (_isOutboundRunning)
            {
                var ans = MessageBox.Show("出笼服务正在运行，切换将自动停止。确认切换到入笼界面？",
                    "确认切换", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ans != MessageBoxResult.Yes) return;
            }

            StopOutbound();
            var inboundWindow = new InboundWindow();
            inboundWindow.Show();
            Close();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            StopOutbound();
            Close();
        }

        private void OpenGlobalParams_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("INFO", "打开参数设置...");
        }

        private void MenuCageDashboard_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("INFO", "打开看板...");
        }

        #endregion

        #region 辅助方法

        private void UpdateStatistics()
        {
            int total = _taskItems.Count;
            int completed = _taskItems.Count(i => i.Status == "完成");

            string progress = total > 0 ? $"{completed}/{total} ({completed * 100.0 / total:F1}%)" : "0/0 (0%)";
            txtParamTaskCount.Text = $"在库数量: {total}";
            txtParamProgress.Text = $"完成进度: {progress}";
        }

        private void UpdateParamDisplay()
        {
            string sortField = AppConfig.IsInitialized
                ? AppConfig.GetStringOrDefault("OutSortField", "Length") : "Length";
            string sortDir = AppConfig.IsInitialized
                ? AppConfig.GetStringOrDefault("OutSortDir", "Desc") : "Desc";

            string fieldText = sortField.Equals("Length", StringComparison.OrdinalIgnoreCase) ? "长边" : "短边";
            string dirText = sortDir.Equals("Desc", StringComparison.OrdinalIgnoreCase) ? "从大到小" : "从小到大";
            txtParamSort.Text = $"排序: {fieldText} {dirText}（config 配置）";
        }

        private void AddHistory(string glassId, float moveDistance, string result)
        {
            _historyItems.Insert(0, new OutboundHistoryViewModel
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                GlassID = glassId,
                MoveDistance = moveDistance,
                Result = result
            });

            while (_historyItems.Count > 100)
                _historyItems.RemoveAt(_historyItems.Count - 1);
        }

        private void DispatchToLog(string level, string msg)
        {
            Dispatcher.InvokeAsync(() => AppendLog(level, msg));
        }

        private void AppendLog(string level, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            txtLog.AppendText($"[{timestamp}] [{level}] {message}\n");
            txtLog.ScrollToEnd();

            if (AppConfig.IsInitialized &&
                AppConfig.GetStringOrDefault("EnableActivityLog", "0") == "1")
            {
                var content = $"[{level}][出笼] {message}";
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    try { _logRepository.Insert(content, level); }
                    catch { }
                });
            }
        }

        #endregion
    }

    #region 视图模型类

    public class OrderFilterItem
    {
        public string OrderId { get; set; } = "";
        public string OrderName { get; set; } = "";
        public bool IsChecked { get; set; }
        /// <summary>下拉复选框显示文本：优先订单名，后附带订单号。</summary>
        public string DisplayText
            => string.IsNullOrWhiteSpace(OrderName)
                ? OrderId
                : $"{OrderName}（{OrderId}）";
    }

    public class OutboundTaskItemViewModel
    {
        public int SequenceNo { get; set; }
        public string FlowCardNo { get; set; } = "";
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public string ID { get; set; } = "";
        public string CageCode { get; set; } = "-";
        public string LayerNo { get; set; } = "-";
        public float TargetPos { get; set; }
        public string Status { get; set; } = "等待";
        public string OrderName { get; set; } = "-";
        public string ClientName { get; set; } = "-";
        public string MaterialID { get; set; } = "";
        public Material? Material { get; set; }
        /// <summary>连组标识，与 Material.GroupID 对应；单独片此字段为 null。</summary>
        public string? GroupID { get; set; }
        /// <summary>是否属于连组（GroupID != null）</summary>
        public bool IsGrouped => !string.IsNullOrEmpty(GroupID);
        /// <summary>UI 展示用：连组时显示组标识符号</summary>
        public string GroupTag => IsGrouped ? "🔗" : "";
    }

    public class OutboundQueueViewModel
    {
        public string CageCode { get; set; } = "";
        public string LayerNo { get; set; } = "";
        public int Count { get; set; }
        public float TargetPos { get; set; }
        public string Status { get; set; } = "等待";
    }

    public class OutboundHistoryViewModel
    {
        public string Time { get; set; } = "";
        public string GlassID { get; set; } = "";
        public float MoveDistance { get; set; }
        public string Result { get; set; } = "";
    }

    #endregion
}