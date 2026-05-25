using GlassWarehouseSystem.ViewModels;
using Microsoft.EntityFrameworkCore;
using System;
using System.Windows;
using System.Windows.Controls;
namespace GlassWarehouseSystem;
using GlassWarehouseSystem.Models;
using GlassWarehouseSystem.Services;
using GlassWarehouseSystem.LocalConfig;
using GlassWarehouseSystem.Config;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.Core.IMessage;
using HslCommunication.ModBus;
using Microsoft.Win32;
using MiniExcelLibs;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Media.Animation;
using HslDataFormat = HslCommunication.Core.DataFormat;

public partial class Quanjvcanshu : Window
{
    private QuanjvcanshuViewModel vm;
    private HslCommunication.ModBus.ModbusTcpNet _modbus;
    private System.Windows.Threading.DispatcherTimer _timer;
    private bool _isAdmin = false;

    public Quanjvcanshu()
    {
        InitializeComponent();
        vm = new QuanjvcanshuViewModel();
        this.DataContext = vm;
        CacheQueryService.ClearCacheByPattern("config:all_data");
        this.Loaded += async (s, e) => await vm.RefreshDataAsync();
        this.Closed += Quanjvcanshu_Closed;
        ApplyPermission();
    }

    private void Quanjvcanshu_Closed(object sender, EventArgs e)
    {
        try
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }

            if (_modbus != null)
            {
                _modbus.ConnectClose();
                _modbus = null;
                System.Diagnostics.Debug.WriteLine("窗口已关闭，PLC 连接已释放");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("关闭窗口时释放资源失败: " + ex.Message);
        }
    }

    private void ApplyPermission()
    {
        Btnjiaozhun.IsEnabled = _isAdmin;
        BtnAdd.IsEnabled = _isAdmin;
        BtnSave.IsEnabled = _isAdmin;
        BtnDelete.IsEnabled = _isAdmin;
        BtnSaveAll.IsEnabled = _isAdmin;
        BtnImport.IsEnabled = _isAdmin;
        BtnAdminLogin.Content = _isAdmin ? "🔓 退出登录" : "🔒 管理员登录";
        BtnAdminLogin.Background = _isAdmin ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.Green;
    }

    private void BtnAdminLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_isAdmin)
        {
            _isAdmin = false;
            ApplyPermission();
            MessageBox.Show("已退出管理员模式，操作已锁定。");
        }
        else
        {
            GlassWarehouseSystem.ControlPlcUser1 loginWin = new GlassWarehouseSystem.ControlPlcUser1();
            if (loginWin.ShowDialog() == true)
            {
                _isAdmin = true;
                ApplyPermission();
                MessageBox.Show("验证通过，管理员权限已开启！", "系统提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async Task<bool> ConnectPlcAsync()
    {
        if (_modbus != null)
            return true;

        string ip = "192.168.1.8";
        int port = 502;
        byte station = 1;
        string formatStr = "CDAB";
        bool isZeroStart = false;

        var plc = AppConfig.GetPrimaryPlc();
        if (plc != null)
        {
            ip = string.IsNullOrWhiteSpace(plc.PlcIp) ? ip : plc.PlcIp.Trim();
            station = (byte)Math.Clamp(plc.Id <= 0 ? 1 : plc.Id, 1, 255);
        }

        try
        {
            using (var db = new GlassWarehouseSystem.Data.WarehouseDbContext())
            {
                var configs = await db.ConfigRows.ToListAsync();

                if (configs != null && configs.Any())
                {
                    var portCfg = configs.FirstOrDefault(c => c.KeyName == "PlcPort");
                    var stationCfg = configs.FirstOrDefault(c => c.KeyName == "PlcStation");

                    if (portCfg != null)
                    {
                        int.TryParse(portCfg.Value, out port);
                        byte.TryParse(stationCfg?.Value ?? station.ToString(), out station);
                        formatStr = configs.FirstOrDefault(c => c.KeyName == "PlcDataFormat")?.Value ?? "CDAB";
                        isZeroStart = configs.FirstOrDefault(c => c.KeyName == "PlcAddressStartWithZero")?.Value?.ToLower() == "true";
                    }
                }
            }

            _modbus = new HslCommunication.ModBus.ModbusTcpNet(ip, port, station)
            {
                AddressStartWithZero = isZeroStart,
                DataFormat = GetFormatFromString(formatStr),
            };

            var result = await Task.Run(() => _modbus.ConnectServer());
            if (result.IsSuccess)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_timer == null)
                    {
                        _timer = new System.Windows.Threading.DispatcherTimer();
                        _timer.Interval = TimeSpan.FromMinutes(1);
                        _timer.Tick += (s, e) =>
                        {
                            _modbus?.ConnectClose();
                            _modbus = null;
                            _timer?.Stop();
                            System.Diagnostics.Debug.WriteLine("PLC 已自动断开");
                        };
                    }
                });

                return true;
            }

            _modbus = null;
            return false;
        }
        catch (Exception ex)
        {
            _modbus = null;
            System.Diagnostics.Debug.WriteLine("连接异常: " + ex.Message);
            return false;
        }
    }

    private HslCommunication.Core.DataFormat GetFormatFromString(string format)
    {
        return format.ToUpper() switch
        {
            "ABCD" => HslCommunication.Core.DataFormat.ABCD,
            "BADC" => HslCommunication.Core.DataFormat.BADC,
            "DCBA" => HslCommunication.Core.DataFormat.DCBA,
            _ => HslCommunication.Core.DataFormat.CDAB,
        };
    }

    private void IODataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IODataGrid.SelectedItems.Count == 0)
        {
            ResetUI();
            return;
        }

        if (IODataGrid.SelectedItems.Count > 0)
        {
            var selected = IODataGrid.SelectedItems[IODataGrid.SelectedItems.Count - 1] as ConfigRow;
            if (selected != null)
            {
                txtConfigID.Text = selected.ConfigID.ToString();
                foreach (ComboBoxItem item in cmbType.Items)
                {
                    if (item.Content.ToString() == selected.Type)
                    {
                        cmbType.SelectedItem = item;
                        break;
                    }
                }
                SetComboBoxSelectedContent(this.cmb2Type, selected.Type2);
                txtKeyName.Text = selected.KeyName;
                txtValue.Text = selected.Value;
                txtAddress.Text = selected.Address?.ToString();
                txtDescribe.Text = selected.Describe;
                dpUpdateTime.SelectedDate = selected.UpdateTime;
            }
        }
    }

    private void SetComboBoxSelectedContent(ComboBox cmb, string? targetContent)
    {
        if (string.IsNullOrWhiteSpace(targetContent))
        {
            cmb.SelectedIndex = -1;
            return;
        }

        foreach (ComboBoxItem item in cmb.Items)
        {
            if (string.Equals(item.Content?.ToString(), targetContent, StringComparison.Ordinal))
            {
                cmb.SelectedItem = item;
                break;
            }
        }
    }

    private void ResetUI()
    {
        IODataGrid.SelectedItems.Clear();
        ClearForm2();
    }

    private void ClearForm2()
    {
        txtConfigID.Text = "（保存后自动生成）";
        txtKeyName.Clear();
        txtValue.Clear();
        txtAddress.Clear();
        txtDescribe.Clear();
        dpUpdateTime.SelectedDate = null;
    }

    private async void BtnCalibrate_Click(object sender, RoutedEventArgs e)
    {
        CacheQueryService.ClearCacheByPattern("config:all_data");
        await vm.RefreshDataAsync();
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        IODataGrid.UnselectAll();
        ClearForm2();
    }

    private async void BtnSaveToPLC_Click(object sender, RoutedEventArgs e)
    {
        CacheQueryService.ClearCacheByPattern("config:all_data");
        await vm.RefreshDataAsync();
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = IODataGrid.SelectedItems.Cast<ConfigRow>().ToList();
        if (!selected.Any())
        {
            MessageBox.Show("请先选择要删除的记录。", "提示");
            return;
        }

        using (var db = new GlassWarehouseSystem.Data.WarehouseDbContext())
        {
            var ids = selected.Select(x => x.ConfigID).ToList();
            var rows = db.ConfigRows.Where(x => ids.Contains(x.ConfigID)).ToList();
            if (rows.Any())
            {
                db.ConfigRows.RemoveRange(rows);
                await db.SaveChangesAsync();
            }
        }

        CacheQueryService.ClearCacheByPattern("config:all_data");
        await vm.RefreshDataAsync();
        ClearForm2();
    }

    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        CacheQueryService.ClearCacheByPattern("config:all_data");
        await vm.RefreshDataAsync();
    }

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        CacheQueryService.ClearCacheByPattern("config:all_data");
        await vm.RefreshDataAsync();
    }

    private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && !row.IsSelected)
        {
            row.IsSelected = true;
        }
    }
}
