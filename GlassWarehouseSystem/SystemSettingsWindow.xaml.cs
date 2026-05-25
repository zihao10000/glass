using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.LocalConfig;
using GlassWarehouseSystem.Services;
using GlassWarehouseSystem.Config;

namespace GlassWarehouseSystem;

public partial class SystemSettingsWindow : Window
{
    private readonly ObservableCollection<PlcConnectionConfig> _plcItems = new();
    private LocalSystemSettings _settings = new();

    public SystemSettingsWindow()
    {
        InitializeComponent();
        lstPlcConnections.ItemsSource = _plcItems;
        Loaded += SystemSettingsWindow_Loaded;
    }

    private void SystemSettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = LocalSystemConfigService.Load();
        LoadDatabaseSettings();
        LoadPlcSettings();
        LoadPrinterSettings();
    }

    private void LoadDatabaseSettings()
    {
        txtDbServerAddress.Text = _settings.Database.ServerAddress;
        txtDbName.Text = _settings.Database.DatabaseName;
        txtDbServerPort.Text = _settings.Database.ServerPort.ToString(CultureInfo.InvariantCulture);
        txtRedisDbIndex.Text = _settings.Database.RedisDbIndex.ToString(CultureInfo.InvariantCulture);
        txtRedisPort.Text = _settings.Database.RedisPort.ToString(CultureInfo.InvariantCulture);
    }

    private void LoadPlcSettings()
    {
        _plcItems.Clear();
        foreach (var item in _settings.PlcConnections.OrderBy(p => p.Id))
            _plcItems.Add(item);

        if (_plcItems.Count > 0)
            lstPlcConnections.SelectedIndex = 0;
    }

    private void LoadPrinterSettings()
    {
        txtPrinterName.Text = _settings.LabelPrinter.PrinterName;
        chkBatchPrint.IsChecked = _settings.LabelPrinter.BatchPrint;
        rbPrintWindows.IsChecked = string.Equals(_settings.LabelPrinter.PrintMode, "Windows", StringComparison.OrdinalIgnoreCase);
        rbPrintNetwork.IsChecked = !rbPrintWindows.IsChecked;
        txtPrinterServicePort.Text = _settings.LabelPrinter.ServicePort.ToString(CultureInfo.InvariantCulture);
        txtPrinterServiceIp.Text = _settings.LabelPrinter.ServiceIp;
        txtPrinterPageWidth.Text = _settings.LabelPrinter.PageWidth.ToString("0.##", CultureInfo.InvariantCulture);
        txtPrinterPageHeight.Text = _settings.LabelPrinter.PageHeight.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void LstPlcConnections_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstPlcConnections.SelectedItem is not PlcConnectionConfig item)
            return;

        txtPlcId.Text = item.Id.ToString(CultureInfo.InvariantCulture);
        txtPlcName.Text = item.Name;
        txtPlcIp.Text = item.PlcIp;
        cmbPlcType.Text = item.Type;
        txtPlcRack.Text = item.Rack.ToString(CultureInfo.InvariantCulture);
        txtPlcSlot.Text = item.Slot.ToString(CultureInfo.InvariantCulture);
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (!CollectDatabaseSettings())
            return;
        if (!CollectPlcSettings())
            return;
        if (!CollectPrinterSettings())
            return;

        try
        {
            LocalSystemConfigService.Save(_settings);
            SyncPlcIpToDatabase();
            AppConfig.ReloadLocalSettings();
            RedisHelper.ResetConnection();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存系统配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show($"配置已保存到本地文件：\n{LocalSystemConfigService.GetConfigPath()}", "提示",
            MessageBoxButton.OK, MessageBoxImage.Information);

        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            // 非 ShowDialog 打开时不能设置 DialogResult，直接关闭窗口即可
        }

        Close();
    }

    private bool CollectDatabaseSettings()
    {
        if (!int.TryParse(txtDbServerPort.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var serverPort))
        {
            MessageBox.Show("服务端口号格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(txtRedisDbIndex.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var redisDbIndex))
        {
            MessageBox.Show("Redis库索引格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(txtRedisPort.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var redisPort))
        {
            MessageBox.Show("Redis端口号格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _settings.Database.ServerAddress = txtDbServerAddress.Text.Trim();
        _settings.Database.DatabaseName = txtDbName.Text.Trim();
        _settings.Database.ServerPort = serverPort;
        _settings.Database.RedisDbIndex = redisDbIndex;
        _settings.Database.RedisPort = redisPort;
        return true;
    }

    private bool CollectPlcSettings()
    {
        if (!int.TryParse(txtPlcId.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            MessageBox.Show("PLC ID 格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(txtPlcRack.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rack))
        {
            MessageBox.Show("PLC 架号格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(txtPlcSlot.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot))
        {
            MessageBox.Show("PLC 槽号格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var selected = lstPlcConnections.SelectedItem as PlcConnectionConfig;
        if (selected == null)
        {
            selected = new PlcConnectionConfig();
            _plcItems.Add(selected);
        }

        selected.Id = id;
        selected.Name = txtPlcName.Text.Trim();
        selected.PlcIp = txtPlcIp.Text.Trim();
        selected.Type = (cmbPlcType.Text ?? string.Empty).Trim();
        selected.Rack = rack;
        selected.Slot = slot;

        _settings.PlcConnections = _plcItems
            .OrderBy(p => p.Id)
            .Select(p => new PlcConnectionConfig
            {
                Id = p.Id,
                Name = p.Name,
                PlcIp = p.PlcIp,
                Type = p.Type,
                Rack = p.Rack,
                Slot = p.Slot
            })
            .ToList();

        lstPlcConnections.Items.Refresh();
        return true;
    }

    private bool CollectPrinterSettings()
    {
        if (!int.TryParse(txtPrinterServicePort.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var servicePort))
        {
            MessageBox.Show("打印服务端口格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!decimal.TryParse(txtPrinterPageWidth.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pageWidth))
        {
            MessageBox.Show("打印页面宽格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!decimal.TryParse(txtPrinterPageHeight.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pageHeight))
        {
            MessageBox.Show("打印页面高格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _settings.LabelPrinter.PrinterName = txtPrinterName.Text.Trim();
        _settings.LabelPrinter.BatchPrint = chkBatchPrint.IsChecked == true;
        _settings.LabelPrinter.PrintMode = rbPrintWindows.IsChecked == true ? "Windows" : "Network";
        _settings.LabelPrinter.ServicePort = servicePort;
        _settings.LabelPrinter.ServiceIp = txtPrinterServiceIp.Text.Trim();
        _settings.LabelPrinter.PageWidth = pageWidth;
        _settings.LabelPrinter.PageHeight = pageHeight;
        return true;
    }

    private void BtnDeletePlc_Click(object sender, RoutedEventArgs e)
    {
        if (lstPlcConnections.SelectedItem is not PlcConnectionConfig selected)
        {
            MessageBox.Show("请先在左侧选择要删除的 PLC 项。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"确认删除 PLC 配置：{selected.DisplayText} ？", "确认删除",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _plcItems.Remove(selected);

        if (_plcItems.Count > 0)
        {
            lstPlcConnections.SelectedIndex = 0;
        }
        else
        {
            txtPlcId.Clear();
            txtPlcName.Clear();
            txtPlcIp.Clear();
            cmbPlcType.SelectedIndex = 0;
            txtPlcRack.Clear();
            txtPlcSlot.Clear();
        }
    }

    private void BtnAddPlc_Click(object sender, RoutedEventArgs e)
    {
        var nextId = _plcItems.Count == 0 ? 1 : _plcItems.Max(p => p.Id) + 1;
        var item = new PlcConnectionConfig
        {
            Id = nextId,
            Name = $"PLC{nextId}",
            PlcIp = string.Empty,
            Type = "S71200",
            Rack = 0,
            Slot = 1
        };

        _plcItems.Add(item);
        lstPlcConnections.SelectedItem = item;
        lstPlcConnections.ScrollIntoView(item);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SyncPlcIpToDatabase()
    {
        var plcIp = _settings.PlcConnections
            .OrderBy(p => p.Id)
            .Select(p => p.PlcIp?.Trim())
            .FirstOrDefault(ip => !string.IsNullOrWhiteSpace(ip));

        if (string.IsNullOrWhiteSpace(plcIp))
            return;

        using var context = new WarehouseDbContext();
        var item = context.ConfigRows.FirstOrDefault(c => c.KeyName == "PlcIp");
        if (item == null)
        {
            item = new GlassWarehouseSystem.Models.ConfigRow
            {
                KeyName = "PlcIp",
                Value = plcIp,
                UpdateTime = DateTime.Now
            };
            context.ConfigRows.Add(item);
        }
        else
        {
            item.Value = plcIp;
            item.UpdateTime = DateTime.Now;
        }

        context.SaveChanges();
    }
}
