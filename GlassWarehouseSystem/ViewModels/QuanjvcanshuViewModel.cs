using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using GlassWarehouseSystem.Services;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;

namespace GlassWarehouseSystem.ViewModels
{
    public class QuanjvcanshuViewModel : DependencyObject
    {
        private const string REDIS_KEY = "config:all_data";

        public QuanjvcanshuViewModel()
        {
            ConfigList = new ObservableCollection<ConfigRow>();
        }

        public static readonly DependencyProperty ConfigListProperty =
            DependencyProperty.Register(
                nameof(ConfigList),
                typeof(ObservableCollection<ConfigRow>),
                typeof(QuanjvcanshuViewModel));

        public ObservableCollection<ConfigRow> ConfigList
        {
            get => (ObservableCollection<ConfigRow>)GetValue(ConfigListProperty);
            set => SetValue(ConfigListProperty, value);
        }

        public async Task RefreshDataAsync()
        {
            try
            {
                var data = await CacheQueryService.GetCachedDataAsync<ConfigRow>(
                    REDIS_KEY,
                    async () =>
                    {
                        using var db = new WarehouseDbContext();
                        return await db.ConfigRows.ToListAsync();
                    },
                    TimeSpan.FromSeconds(5));

                ConfigList.Clear();
                foreach (var item in data)
                    ConfigList.Add(item);
            }
            catch (Exception ex)
            {
                MessageBox.Show("获取数据失败: " + ex.Message);
            }
        }

        public async Task SaveAllAsync()
        {
            try
            {
                using var db = new WarehouseDbContext();
                foreach (var item in ConfigList)
                {
                    if (item.ConfigID == 0)
                        db.ConfigRows.Add(item);
                    else
                        db.Entry(item).State = EntityState.Modified;
                }

                await db.SaveChangesAsync();
                CacheQueryService.ClearCacheByPattern(REDIS_KEY);
                await RefreshDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败: " + ex.Message);
            }
        }
    }
}
