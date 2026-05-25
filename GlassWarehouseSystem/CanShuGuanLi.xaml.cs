using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GlassWarehouseSystem.Services; // 引入正规的缓存服务
using GlassWarehouseSystem.Data;     // 引入数据库上下文
using Microsoft.EntityFrameworkCore; // 引入 AsNoTracking
using GlassWarehouseSystem.Models;

namespace GlassWarehouseSystem
{
    public partial class CanShuGuanLi : Window
    {
        public CanShuGuanLi()
        {
            InitializeComponent();

            // 【只用一次的临时代码】强制清空昨天的脏缓存！
            //GlassWarehouseSystem.Services.RedisHelper.Db.Execute("FLUSHALL");

            // 默认加载所有参数总览
            LoadOverviewData();
        }

        #region 左侧菜单点击事件

        private void OnSystemSubItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn)
                LoadDataByCategory(btn.Content.ToString());
        }

        private void OnPlcSubItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn)
                LoadDataByCategory(btn.Content.ToString());
        }

        private void OnOverviewClick(object sender, RoutedEventArgs e)
        {
            LoadOverviewData();
        }

        #endregion

        #region 核心：基于缓存与数据库的数据加载逻辑

        /// <summary>
        /// 根据参数类别（如"检测台设备参数"）加载数据
        /// </summary>
        private void LoadDataByCategory(string categoryName)
        {
            string cacheKey = $"SystemParam:{categoryName}";

            try
            {
                // 使用通用的缓存查询服务
                var data = CacheQueryService.GetCachedData(
                    cacheKey,
                    () =>
                    {
                        using (var context = new WarehouseDbContext())
                        {
                            // 去 SQL 数据库查该类别下的所有参数
                            var dbParams = context.ConfigRows
                            .AsNoTracking()
                            .ToList();

                            // 转换为界面需要的 ViewModel 列表
                            return new List<ParamItem>
                            {
                                new ParamItem
                                {
                                    Name = "测量误差",
                                 //改  // Value = dbParams.FirstOrDefault()?.MeasureErrorAllowance?.ToString(),
                                    Unit = "mm",
                                    Description = "测量允许误差"
                                },
                                new ParamItem
                                {
                                    Name = "玻璃间距",
                                //改   // Value = dbParams.FirstOrDefault()?.GlassSpacing?.ToString(),
                                    Unit = "mm",
                                    Description = "玻璃间隔"
                                }
                            };
                        }
                    },
                    TimeSpan.FromMinutes(30) // 参数不常变，可以放心地在 Redis 缓存 30 分钟
                );

                dgParams.ItemsSource = data;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载参数数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载所有参数总览
        /// </summary>
        //private void LoadOverviewData()
        //{
        //    try
        //    {
        //        var data = CacheQueryService.GetCachedData(
        //            "SystemParam:AllOverview",
        //            () =>
        //            {
        //                using (var context = new WarehouseDbContext())
        //                {
        //                    // 查出全表所有参数
        //                    var dbParams = context.SystemParameters
        //                        .AsNoTracking()
        //                        .ToList();

        //                    return dbParams.Select(p => new ParamItem
        //                    {
        //                        Name = p.Name,
        //                        Unit = p.Unit,
        //                        Value = p.Value,
        //                        Description = p.Description
        //                    }).ToList();
        //                }
        //            },
        //            TimeSpan.FromMinutes(30)
        //        );

        //        dgParams.ItemsSource = data;
        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show($"加载总览数据失败: {ex.Message}");
        //    }
        //}
        private void LoadOverviewData()
        {
            try
            {
                var data = CacheQueryService.GetCachedData(
                    "SystemConfig:Overview",
                    () =>
                    {
                        using (var context = new WarehouseDbContext())
                        {
                            // 获取配置表的第一行（或默认行）
                            var config = context.ConfigRows.AsNoTracking().FirstOrDefault();
                            var list = new List<ParamItem>();

                            if (config != null)
                            {
                             //改   // 将对象的每个属性手动转为列表项显示在 Grid 中
                                //list.Add(new ParamItem { Name = "测量误差余量", Value = config.MeasureErrorAllowance?.ToString(), Unit = "mm", Description = "测量误差允许范围" });
                                //list.Add(new ParamItem { Name = "数据源类型", Value = config.DataSourceType?.ToString(), Unit = "-", Description = "1:本地 2:远程" });
                                //list.Add(new ParamItem { Name = "最大扫描重试次数", Value = config.MaxScanRetryTimes?.ToString(), Unit = "次", Description = "PLC通讯失败重试" });
                                //list.Add(new ParamItem { Name = "笼内距离K值", Value = config.CageInnerDistanceK?.ToString(), Unit = "-", Description = "物理距离系数" });
                                //list.Add(new ParamItem { Name = "玻璃间距", Value = config.GlassSpacing?.ToString(), Unit = "mm", Description = "入笼最小间距" });
                            }
                            return list;
                        }
                    },
                    TimeSpan.FromMinutes(30)
                );

                dgParams.ItemsSource = data;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载配置数据失败: {ex.Message}");
            }
        }

        #endregion

        #region 其他按钮事件

        private void OnBackupClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("可以结合数据库做参数全量导出备份！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnRestoreClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("可以从备份文件直接写入 SQL 并清空 Redis 缓存实现还原！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion
    }

    // 参数项数据模型（给 XAML 界面绑定的）
    public class ParamItem
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string Unit { get; set; }
        public string Description { get; set; }
    }
}