using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;

namespace GlassWarehouseSystem
{
    public partial class LogWindow : Window
    {
        public ObservableCollection<LogsViewModel> LogList { get; set; } = new ObservableCollection<LogsViewModel>();

        public LogWindow()
        {
            InitializeComponent();
            this.DataContext = this;
            dpStart.SelectedDate = null;
            dpEnd.SelectedDate = null;
            _ = LoadLogsAsync();
        }

        // 🔍 查询按钮
        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            await LoadLogsAsync();
        }

        // 🔄 重置/刷新按钮
        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            dpStart.SelectedDate = null;
            dpEnd.SelectedDate = null;
            cmbLogLevel.SelectedIndex = 0;
            txtKeyword.Clear();
            _ = LoadLogsAsync();
        }

        // 🗑️ 修改后的【多选清除】按钮逻辑
        private async void BtnClearDisplay_Click(object sender, RoutedEventArgs e)
        {
            // 1. 获取所有选中的行（转换为列表，防止删除时集合改变报错）
            var selectedLogs = LogDataGrid.SelectedItems.Cast<LogsViewModel>().ToList();

            if (selectedLogs.Count == 0)
            {
                MessageBox.Show("请先选中要删除的行（可按住Ctrl多选）！", "提示");
                return;
            }

            // 2. 确认提示（显示具体条数）
            var result = MessageBox.Show($"确定要删除选中的 {selectedLogs.Count} 条记录吗？\n(将从数据库同步删除)",
                                         "批量删除确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    using (var db = new WarehouseDbContext())
                    {
                        foreach (var item in selectedLogs)
                        {
                            // 3. 数据库删除
                            var dbLog = await db.Logs.FirstOrDefaultAsync(l => l.LogID == item.LogID);
                            if (dbLog != null)
                            {
                                db.Logs.Remove(dbLog);
                            }
                            // 4. 界面列表删除
                            LogList.Remove(item);
                        }
                        // 5. 统一保存数据库更改
                        await db.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("批量删除失败: " + ex.Message);
                }
            }
        }

        // 🧨 清除数据库所有日志
        private async void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("警告：确定要永久清空数据库中【所有】日志记录吗？", "极其重要提示", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    using (var db = new WarehouseDbContext())
                    {
                        db.Logs.RemoveRange(db.Logs);
                        await db.SaveChangesAsync();
                    }
                    LogList.Clear();
                    MessageBox.Show("所有记录已成功清空。");
                }
                catch (Exception ex) { MessageBox.Show("删除失败: " + ex.Message); }
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // 读取数据库并排序
        private async Task LoadLogsAsync()
        {
            try
            {
                using (var db = new WarehouseDbContext())
                {
                    var query = db.Logs.AsQueryable();

                    if (dpStart.SelectedDate.HasValue)
                        query = query.Where(l => l.RecordTime >= dpStart.SelectedDate.Value.Date);
                    if (dpEnd.SelectedDate.HasValue)
                        query = query.Where(l => l.RecordTime < dpEnd.SelectedDate.Value.Date.AddDays(1));

                    var selectedItem = cmbLogLevel.SelectedItem as ComboBoxItem;
                    string level = selectedItem?.Content.ToString();
                    if (level != "全部" && !string.IsNullOrEmpty(level))
                        query = query.Where(l => l.Type == level);

                    string keyword = txtKeyword.Text.Trim();
                    if (!string.IsNullOrEmpty(keyword))
                        query = query.Where(l => l.LogContent.Contains(keyword));

                    // --- 【修改：排序逻辑】按编号倒序排列（最新的编号在第一行） ---
                    var rawData = await query.OrderByDescending(l => l.LogID).ToListAsync();

                    LogList.Clear();
                    foreach (var log in rawData)
                    {
                        LogList.Add(new LogsViewModel
                        {
                            LogID = log.LogID,
                            Type = log.Type,
                            RecordTime = log.RecordTime,
                            LogContent = log.LogContent,
                            TypeColor = GetColorByType(log.Type)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("数据加载失败: " + ex.Message);
            }
        }

        private string GetColorByType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "#909399";
            string t = type.Trim().ToLower();
            if (t.Contains("info") || t.Contains("信息")) return "#409EFF";
            if (t.Contains("warn") || t.Contains("警告")) return "#E6A23C";
            if (t.Contains("error") || t.Contains("错误")) return "#F56C6C";
            return "#909399";
        }
    }

    public class LogsViewModel
    {
        public int LogID { get; set; }
        public string Type { get; set; }
        public DateTime? RecordTime { get; set; }
        public string LogContent { get; set; }
        public string TypeColor { get; set; }
    }
}