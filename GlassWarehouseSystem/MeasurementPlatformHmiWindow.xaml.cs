using Accessibility;
using GlassWarehouseSystem.Config;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.LocalConfig;
using GlassWarehouseSystem.Services;
using HslCommunication;
using HslCommunication.ModBus;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HslDataFormat = HslCommunication.Core.DataFormat;

namespace GlassWarehouseSystem;

/// <summary>
/// 测量台手动 HMI。PLC 固定 IP 192.168.1.10；寄存器地址从 <c>config</c> 表 <c>KeyName</c> 读取（与 files_1 相同 HSL ModbusTcpNet）。
/// </summary>
public partial class MeasurementPlatformHmiWindow : Window
{
    private LocalSystemSettings _settings = LocalSystemConfigService.Load();

    private string DefaultPlcIp = "192.168.1.10";
    private const int DefaultPlcPort = 502;
    private const byte DefaultStation = 1;

    private static readonly Brush BrushInactive = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));
    private static readonly Brush BrushActive = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

    private readonly PlcService _modbus = new PlcService(PlcClient.Instance);
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _pollTimer;
    private string? _activeJogKey;
    private Button? _activeJogButton;
    /// <summary>
    /// ///////////////
    /// </summary>

    public MeasurementPlatformHmiWindow()
    {
        InitializeComponent();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => RefreshClock();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _pollTimer.Tick += (_, _) => PollPositions();

        Loaded += MeasurementPlatformHmiWindow_Loaded;
        ChkMeasureMode.Checked += ChkMeasureMode_Changed;
        ChkMeasureMode.Unchecked += ChkMeasureMode_Changed;
        RbCageA_X_Conveyor.Checked += CageA_XSelect_CheckedChanged;
        RbCageA_X_Conveyor.Unchecked += CageA_XSelect_CheckedChanged;
        RbCageA_X_Measure.Checked += CageA_XSelect_CheckedChanged;
        RbCageA_X_Measure.Unchecked += CageA_XSelect_CheckedChanged;
        RbCageA_X_OneWay.Checked += CageA_XSelect_CheckedChanged;
        RbCageA_X_OneWay.Unchecked += CageA_XSelect_CheckedChanged;
        RbCageA_X_Linked.Checked += CageA_XSelect_CheckedChanged;
        RbCageA_X_Linked.Unchecked += CageA_XSelect_CheckedChanged;
        RbCageB_X_Conveyor.Checked += CageB_XSelect_CheckedChanged;
        RbCageB_X_Conveyor.Unchecked += CageB_XSelect_CheckedChanged;
        RbCageB_X_Measure.Checked += CageB_XSelect_CheckedChanged;
        RbCageB_X_Measure.Unchecked += CageB_XSelect_CheckedChanged;
        RbCageB_X_OneWay.Checked += CageB_XSelect_CheckedChanged;
        RbCageB_X_OneWay.Unchecked += CageB_XSelect_CheckedChanged;
        RbCageB_X_Linked.Checked += CageB_XSelect_CheckedChanged;
        RbCageB_X_Linked.Unchecked += CageB_XSelect_CheckedChanged;
        RbOut_X_OneWay.Checked += Out_XSelect_CheckedChanged;
        RbOut_X_OneWay.Unchecked += Out_XSelect_CheckedChanged;
        RbOut_X_Out.Checked += Out_XSelect_CheckedChanged;
        RbOut_X_Out.Unchecked += Out_XSelect_CheckedChanged;
        RbOut_X_Linked.Checked += Out_XSelect_CheckedChanged;
        RbOut_X_Linked.Unchecked += Out_XSelect_CheckedChanged;
        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            _pollTimer.Stop();
            StopJog();

        };
    }

    private void MeasurementPlatformHmiWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshClock();
        _clockTimer.Start();
        LoadCageAisles("A", TxtCageA_CurrentAisle, PnlCageA_Aisles);
        LoadCageAisles("B", TxtCageB_CurrentAisle, PnlCageB_Aisles);
        RefreshManualControlsEnabled();
        RefreshZOriginButtonsEnabled();
    }

    private void RefreshClock()
    {
        var now = DateTime.Now;
        TxtClockDate.Text = now.ToString("yyyy/MM/dd");
        TxtClockTime.Text = now.ToString("HH:mm:ss");
    }

    private void LoadCageAisles(string locationType, TextBox currentAisleTarget, Panel aislePanel)
    {
        aislePanel.Children.Clear();
        currentAisleTarget.Text = "0";

        try
        {
            using var context = new WarehouseDbContext();
            var cage = context.Cages
                .Where(c => c.LocationType != null)
                .AsEnumerable()
                .FirstOrDefault(c => string.Equals((c.LocationType ?? string.Empty).Trim(), locationType, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals((c.LocationType ?? string.Empty).Trim(), locationType + "笼", StringComparison.OrdinalIgnoreCase));

            if (cage == null)
                return;

            var layers = context.Layers
                .Where(l => l.CageID == cage.CageCode)
                .OrderBy(l => l.LayerNo)
                .Select(l => new { l.LayerNo, l.Coordinate })
                .ToList();

            foreach (var layer in layers)
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 2)
                };

                row.Children.Add(new TextBlock
                {
                    Text = (layer.LayerNo ?? 0).ToString(CultureInfo.InvariantCulture),
                    Width = 32,
                    VerticalAlignment = VerticalAlignment.Center
                });

                row.Children.Add(new TextBox
                {
                    Width = 120,
                    Height = 28,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xE6, 0xC9)),
                    BorderThickness = new Thickness(1),
                    IsReadOnly = true,
                    Text = (layer.Coordinate ?? 0m).ToString("F2", CultureInfo.InvariantCulture)
                });

                aislePanel.Children.Add(row);
            }

            if (layers.Count > 0)
            {
                currentAisleTarget.Text = (layers[0].LayerNo ?? 0).ToString(CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            aislePanel.Children.Clear();
            currentAisleTarget.Text = "0";
        }
    }

    private void ChkManualTestEnable_Changed(object sender, RoutedEventArgs e)
    {
        // 不管当前是否按住，都先确保 jog 端口写 0
        StopJog();

        if (_modbus == null)
        {
            RefreshManualControlsEnabled();
            return;
        }

        var addr = HmiPlcConfigService.TryGetModbusAddress(HmiPlcConfigService.KeyTestEnable);
        if (string.IsNullOrEmpty(addr))
        {
            MessageBox.Show("数据库 config 中缺少 Addr_Test_Enable 的地址配置。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshManualControlsEnabled();
            return;
        }

        bool enable = ChkManualTestEnable.IsChecked == true;
        if (enable)
        {
            // 按需求：先读一次，再写 1
            try { _modbus.ReadBool_Hmi(addr); } catch { /* ignore */ }

        }

        RefreshManualControlsEnabled();
    }

    private void ChkZOriginEnable_Changed(object sender, RoutedEventArgs e) => RefreshZOriginButtonsEnabled();

    private void ChkMeasureMode_Changed(object? sender, RoutedEventArgs e)
    {
        if (_modbus == null)
            return;

        var addr = HmiPlcConfigService.TryGetModbusAddress(HmiPlcConfigService.KeyMeasureModeEnable);
        if (string.IsNullOrEmpty(addr))
            return;

        bool enable = ChkMeasureMode.IsChecked == true;
        _modbus.Write_Hmi(addr, enable);
    }

    private void RefreshManualControlsEnabled()
    {
        bool en = ChkManualTestEnable.IsChecked == true && _modbus != null;
        PnlCageA_ManualHost.IsEnabled = en;
        PnlCageB_ManualHost.IsEnabled = en;
        PnlStationOut.IsEnabled = en;
        BtnIndZPlus.IsEnabled = en;
        BtnIndZMinus.IsEnabled = en;
        BtnIndConvXm.IsEnabled = en;
        BtnIndConvXp.IsEnabled = en;
        BtnIndMeasXm.IsEnabled = en;
        BtnIndMeasXp.IsEnabled = en;
        BtnLnkZPlus.IsEnabled = en;
        BtnLnkZMinus.IsEnabled = en;
        BtnLnkXm.IsEnabled = en;
        BtnLnkXp.IsEnabled = en;
        BtnLnkXExec.IsEnabled = en;
        BtnLnkZExec.IsEnabled = en;
        BtnOutManualXMinus.IsEnabled = en;
        BtnOutManualXPlus.IsEnabled = en;
        BtnOutXMinus.IsEnabled = en;
        BtnOutXPlus.IsEnabled = en;
        BtnOutXExec.IsEnabled = en;
        RbOut_X_OneWay.IsEnabled = en;
        RbOut_X_Out.IsEnabled = en;
        RbOut_X_Linked.IsEnabled = en;
        RefreshCageAYOriginSetEnabled();
        RefreshCageBYOriginSetEnabled();
    }

    private void RefreshZOriginButtonsEnabled()
    {
        bool en = ChkZOriginEnable.IsChecked == true && _modbus != null;
        BtnZOriginSet.IsEnabled = en;
        BtnXClearSet.IsEnabled = en;
    }

    private void RefreshCageAYOriginSetEnabled()
    {
        bool en = ChkManualTestEnable.IsChecked == true && _modbus != null && ChkCageA_YOriginEnable.IsChecked == true;
        BtnCageA_YOriginSet.IsEnabled = en;
    }

    private void RefreshCageBYOriginSetEnabled()
    {
        bool en = ChkManualTestEnable.IsChecked == true && _modbus != null && ChkCageB_YOriginEnable.IsChecked == true;
        BtnCageB_YOriginSet.IsEnabled = en;
    }



    private string LoadPlcIpFromConfig()
    {
        try
        {
            using var context = new WarehouseDbContext();
            var value = context.ConfigRows
                .Where(c => c.KeyName == "PlcIp")
                .Select(c => c.Value)
                .FirstOrDefault();

            var ip = value?.Trim();
            return string.IsNullOrWhiteSpace(ip) ? DefaultPlcIp : ip;
        }
        catch
        {
            return DefaultPlcIp;
        }
    }

    private void PollPositions()
    {
        if (_modbus == null)
            return;

        if (PnlStationMeasure.Visibility == Visibility.Visible)
        {
            // 底部实时显示：只读取传送台/测量台位置
            //TryReadAnyToText(HmiPlcConfigService.KeyTransXPlus, TxtPosConveyor);
            //TryReadAnyToText(HmiPlcConfigService.KeyXPosSet, TxtPosMeasure);
            TryReadFloatToText(HmiPlcConfigService.KeyTransPos, TxtPosConveyor);
            TryReadFloatToText(HmiPlcConfigService.KeyMeasurePos, TxtPosMeasure);
            TryReadFloatToText(HmiPlcConfigService.KeyZPos, TxtPosZ);
        }

        if (PnlStationCageA.Visibility == Visibility.Visible)
        {
            //TryReadAnyToText(HmiPlcConfigService.KeyCageAYMinus, TxtCageA_StatusConveyor);
            //TryReadAnyToText(HmiPlcConfigService.KeyCageAXSelectMeasure, TxtCageA_StatusMeasure);
            TryReadFloatToText(HmiPlcConfigService.KeyTransPos, TxtCageA_StatusConveyor);
            TryReadFloatToText(HmiPlcConfigService.KeyMeasurePos, TxtCageA_StatusMeasure);
            TryReadFloatToText(HmiPlcConfigService.KeyOneWayPos, TxtCageA_StatusOneWay);
            TryReadFloatToText(HmiPlcConfigService.KeyCageAYRealtime, TxtCageA_YRealtime);
        }

        if (PnlStationCageB.Visibility == Visibility.Visible)
        {
            TryReadFloatToText(HmiPlcConfigService.KeyTransPos, TxtCageB_StatusConveyor);
            TryReadFloatToText(HmiPlcConfigService.KeyMeasurePos, TxtCageB_StatusMeasure);
            TryReadFloatToText(HmiPlcConfigService.KeyOneWayPos, TxtCageB_StatusOneWay);
            TryReadFloatToText(HmiPlcConfigService.KeyCageBYRealtime, TxtCageB_YRealtime);
        }
    }

    private void TryReadFloatToText(string configKey, TextBox target)
    {
        try
        {
            var addr = HmiPlcConfigService.TryGetModbusAddress(configKey);
            if (string.IsNullOrEmpty(addr))
                return;
            var r = _modbus!.ReadFloat(addr);
            target.Text = r.ToString("F2", CultureInfo.InvariantCulture);
        }
        catch
        {
            // 轮询失败时保持上次显示
        }
    }

    private bool TryWriteFloatByKey(string configKey, float value, string actionName)
    {
        if (_modbus == null)
            return false;

        var addr = HmiPlcConfigService.TryGetModbusAddress(configKey);
        if (string.IsNullOrEmpty(addr))
        {
            MessageBox.Show($"数据库 config 中缺少 {configKey} 的地址配置。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _modbus.Write_Hmi(addr, value);

        return true;
    }

    private bool TryGetAisleCoordinate(string locationType, int layerNo, out float coordinate)
    {
        coordinate = 0f;

        try
        {
            using var context = new WarehouseDbContext();
            var cage = context.Cages
                .Where(c => c.LocationType != null)
                .AsEnumerable()
                .FirstOrDefault(c => string.Equals((c.LocationType ?? string.Empty).Trim(), locationType, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals((c.LocationType ?? string.Empty).Trim(), locationType + "笼", StringComparison.OrdinalIgnoreCase));

            if (cage == null)
                return false;

            var layer = context.Layers
                .Where(l => l.CageID == cage.CageCode && l.LayerNo == layerNo)
                .Select(l => l.Coordinate)
                .FirstOrDefault();

            if (!layer.HasValue)
                return false;

            coordinate = (float)Math.Round(layer.Value, 2, MidpointRounding.AwayFromZero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryParseTargetFloat(TextBox source, string fieldName, out float value)
    {
        value = 0f;
        if (!decimal.TryParse(source.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            MessageBox.Show($"{fieldName}格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        d = Math.Round(d, 2, MidpointRounding.AwayFromZero);
        value = (float)d;
        source.Text = value.ToString("F2", CultureInfo.InvariantCulture);
        return true;
    }

    private bool TryParseAisleNo(TextBox source, string fieldName, out int aisleNo)
    {
        aisleNo = 0;
        if (!int.TryParse(source.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out aisleNo))
        {
            MessageBox.Show($"{fieldName}只能输入整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        source.Text = aisleNo.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    /// 按 config.Type 自动读取并显示（用于调试/测试按键写入是否有效）。
    /// 支持 Bool/Int/Short/Float/Double/String（大小写不敏感）。
    /// </summary>
    //private void TryReadAnyToText(string configKey, TextBox target)
    //{
    //    if (_modbus == null)
    //        return;

    //    try
    //    {
    //        var (addr, type) = HmiPlcConfigService.TryGetModbusAddressAndType(configKey);
    //        if (string.IsNullOrEmpty(addr))
    //            return;

    //        var t = (type ?? string.Empty).Trim().ToLowerInvariant();
    //        switch (t)
    //        {
    //            case "bool":
    //            case "boolean":
    //                {
    //                    var r = _modbus.ReadBool(addr);
    //                    if (r.IsSuccess)
    //                        target.Text = r.Content ? "1" : "0";
    //                    break;
    //                }
    //            case "short":
    //            case "int16":
    //                {
    //                    var r = _modbus.ReadInt16(addr);
    //                    if (r.IsSuccess)
    //                        target.Text = r.Content.ToString(CultureInfo.InvariantCulture);
    //                    break;
    //                }
    //            case "int":
    //            case "int32":
    //                {
    //                    var r = _modbus.ReadInt32(addr);
    //                    if (r.IsSuccess)
    //                        target.Text = r.Content.ToString(CultureInfo.InvariantCulture);
    //                    break;
    //                }
    //            case "float":
    //            case "single":
    //                {
    //                    var r = _modbus.ReadFloat(addr);
    //                    if (r.IsSuccess)
    //                        target.Text = r.Content.ToString("F2", CultureInfo.InvariantCulture);
    //                    break;
    //                }
    //            case "double":
    //                {
    //                    var r = _modbus.ReadDouble(addr);
    //                    if (r.IsSuccess)
    //                        target.Text = r.Content.ToString("F2", CultureInfo.InvariantCulture);
    //                    break;
    //                }
    //            case "string":
    //                {
    //                    // 这里长度无法从配置得知，调试用给一个保守长度
    //                    var r = _modbus.ReadString(addr, 32);
    //                    if (r.IsSuccess)
    //                        target.Text = r.Content ?? string.Empty;
    //                    break;
    //                }
    //            default:
    //                // 未知类型：不做读取，避免误读
    //                break;
    //        }
    //    }
    //    catch
    //    {
    //        // 调试读取失败时保持上次显示
    //    }
    //}

    private void JogIndependent_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanJog())
            return;
        if (sender is not Button btn)
            return;

        _activeJogKey = btn.Name switch
        {
            nameof(BtnIndConvXm) => HmiPlcConfigService.KeyTransXMinus,
            nameof(BtnIndConvXp) => HmiPlcConfigService.KeyTransXPlus,
            nameof(BtnIndMeasXm) => HmiPlcConfigService.KeyMeasureXMinus,
            nameof(BtnIndMeasXp) => HmiPlcConfigService.KeyMeasureXPlus,
            nameof(BtnIndZPlus) => HmiPlcConfigService.KeyMeasureZPlus,
            nameof(BtnIndZMinus) => HmiPlcConfigService.KeyMeasureZMinus,
            _ => null
        };

        if (string.IsNullOrEmpty(_activeJogKey))
            return;

        btn.CaptureMouse();
        TryWriteBoolByKey(_activeJogKey, true);
        e.Handled = true;
    }

    private void JogLinked_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanJog())
            return;
        if (sender is not Button btn)
            return;

        _activeJogKey = btn.Name switch
        {
            nameof(BtnLnkXm) => HmiPlcConfigService.KeyLinkedXMinus,
            nameof(BtnLnkXp) => HmiPlcConfigService.KeyLinkedXPlus,
            nameof(BtnLnkZPlus) => HmiPlcConfigService.KeyLinkedZPlus,
            nameof(BtnLnkZMinus) => HmiPlcConfigService.KeyLinkedZMinus,
            _ => null
        };

        if (string.IsNullOrEmpty(_activeJogKey))
            return;

        btn.CaptureMouse();
        TryWriteBoolByKey(_activeJogKey, true);
        e.Handled = true;
    }

    private void JogCage_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button btn)
            return;

        var configKey = btn.Name switch
        {
            nameof(BtnCageA_LiftUp) => HmiPlcConfigService.KeyCageALiftUp,
            nameof(BtnCageA_LiftDown) => HmiPlcConfigService.KeyCageALiftDown,
            nameof(BtnCageA_XMinus) => HmiPlcConfigService.KeyCageAXMinus,
            nameof(BtnCageA_XPlus) => HmiPlcConfigService.KeyCageAXPlus,
            nameof(BtnCageA_YPlus) => HmiPlcConfigService.KeyCageAYPlus,
            nameof(BtnCageA_YMinus) => HmiPlcConfigService.KeyCageAYMinus,
            nameof(BtnCageB_LiftUp) => HmiPlcConfigService.KeyCageBLiftUp,
            nameof(BtnCageB_LiftDown) => HmiPlcConfigService.KeyCageBLiftDown,
            nameof(BtnCageB_XMinus) => HmiPlcConfigService.KeyCageBXMinus,
            nameof(BtnCageB_XPlus) => HmiPlcConfigService.KeyCageBXPlus,
            nameof(BtnCageB_YPlus) => HmiPlcConfigService.KeyCageBYPlus,
            nameof(BtnCageB_YMinus) => HmiPlcConfigService.KeyCageBYMinus,
            _ => null
        };

        StartJog(btn, configKey);
        e.Handled = true;
    }

    private void JogOut_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button btn)
            return;

        var configKey = btn.Name switch
        {
            nameof(BtnOutManualXMinus) => HmiPlcConfigService.KeyOutManualXMinus,
            nameof(BtnOutManualXPlus) => HmiPlcConfigService.KeyOutManualXPlus,
            nameof(BtnOutXMinus) => HmiPlcConfigService.KeyOutXMinus,
            nameof(BtnOutXPlus) => HmiPlcConfigService.KeyOutXPlus,
            _ => null
        };

        StartJog(btn, configKey);
        e.Handled = true;
    }

    private void Jog_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        StopJog();
        e.Handled = true;
    }

    private void Jog_LostMouseCapture(object sender, RoutedEventArgs e) => StopJog();

    private bool CanJog() =>
        ChkManualTestEnable.IsChecked == true && _modbus != null;

    private void StopJog()
    {
        if (!string.IsNullOrEmpty(_activeJogKey) && _modbus != null)
        {
            // 按住写 1，松开写 0
            TryWriteBoolByKey(_activeJogKey, false);
        }
        _activeJogKey = null;
        _activeJogButton = null;

        if (Mouse.Captured is UIElement u)
            u.ReleaseMouseCapture();
    }

    private void StartJog(Button btn, string? configKey)
    {
        if (!CanJog() || string.IsNullOrEmpty(configKey))
            return;

        StopJog();
        _activeJogKey = configKey;
        _activeJogButton = btn;
        btn.CaptureMouse();
        TryWriteBoolByKey(configKey, true);
    }

    private void SetBoolSelector(string configKey, bool value)
    {
        if (_modbus == null)
            return;

        TryWriteBoolByKey(configKey, value);
    }

    private void CageA_XSelect_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_modbus == null)
            return;

        if (sender is not RadioButton rb)
            return;

        var key = rb.Name switch
        {
            nameof(RbCageA_X_Conveyor) => HmiPlcConfigService.KeyCageAXSelectConveyor,
            nameof(RbCageA_X_Measure) => HmiPlcConfigService.KeyCageAXSelectMeasure,
            nameof(RbCageA_X_OneWay) => HmiPlcConfigService.KeyCageAXSelectOneWay,
            nameof(RbCageA_X_Linked) => HmiPlcConfigService.KeyCageAXSelectLinked,
            _ => null
        };

        if (string.IsNullOrEmpty(key))
            return;

        SetBoolSelector(key, rb.IsChecked == true);
    }

    private void CageB_XSelect_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_modbus == null)
            return;

        if (sender is not RadioButton rb)
            return;

        var key = rb.Name switch
        {
            nameof(RbCageB_X_Conveyor) => HmiPlcConfigService.KeyCageBXSelectConveyor,
            nameof(RbCageB_X_Measure) => HmiPlcConfigService.KeyCageBXSelectMeasure,
            nameof(RbCageB_X_OneWay) => HmiPlcConfigService.KeyCageBXSelectOneWay,
            nameof(RbCageB_X_Linked) => HmiPlcConfigService.KeyCageBXSelectLinked,
            _ => null
        };

        if (string.IsNullOrEmpty(key))
            return;

        SetBoolSelector(key, rb.IsChecked == true);
    }

    private void Out_XSelect_CheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_modbus == null)
            return;

        if (sender is not RadioButton rb)
            return;

        var key = rb.Name switch
        {
            nameof(RbOut_X_OneWay) => HmiPlcConfigService.KeyOutXSelectOneWay,
            nameof(RbOut_X_Out) => HmiPlcConfigService.KeyOutXSelectOut,
            nameof(RbOut_X_Linked) => HmiPlcConfigService.KeyOutXSelectLinked,
            _ => null
        };

        if (string.IsNullOrEmpty(key))
            return;

        SetBoolSelector(key, rb.IsChecked == true);
    }

    private void TryWriteBoolByKey(string configKey, bool value)
    {
        if (_modbus == null)
            return;

        var addr = HmiPlcConfigService.TryGetModbusAddress(configKey);
        if (string.IsNullOrEmpty(addr))
            return;

        _modbus.Write_Hmi(addr, value);

    }

    private void BtnZOriginSet_Click(object sender, RoutedEventArgs e)
    {
        if (ChkZOriginEnable.IsChecked != true)
            return;

        if (!TryParseTargetFloat(TxtPosZ, "Z轴位置", out var value))
            return;

        TryWriteFloatByKey(HmiPlcConfigService.KeyZOriginSet, value, "Z轴原点设置");
    }

    private void BtnXClearSet_Click(object sender, RoutedEventArgs e)
    {
        if (ChkZOriginEnable.IsChecked != true)
            return;

        var addr = HmiPlcConfigService.TryGetModbusAddress(HmiPlcConfigService.KeyXPosSet);
        if (string.IsNullOrEmpty(addr))
        {
            MessageBox.Show("数据库 config 中缺少 Addr_XPos_Set 的地址配置。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _modbus!.Write_Hmi(addr, 0f);

    }

    private void BtnLnkXExec_Click(object sender, RoutedEventArgs e)
    {
        if ( ChkManualTestEnable.IsChecked != true)
            return;

        if (!decimal.TryParse(TxtTargetX.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            MessageBox.Show("X 轴目标位置格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // 按需求：写入文本框的二位小数
        d = Math.Round(d, 2, MidpointRounding.AwayFromZero);
        float v = (float)d;

        var addr = HmiPlcConfigService.TryGetModbusAddress(HmiPlcConfigService.KeyXPosSet);
        if (string.IsNullOrEmpty(addr))
        {
            MessageBox.Show("数据库 config 中缺少 Addr_XPos_Set 的地址配置。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _modbus!.Write_Hmi(addr, v);

    }

    private void BtnLnkZExec_Click(object sender, RoutedEventArgs e)
    {
        if ( ChkManualTestEnable.IsChecked != true)
            return;

        if (!decimal.TryParse(TxtTargetZ.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            MessageBox.Show("Z 轴目标位置格式无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        d = Math.Round(d, 2, MidpointRounding.AwayFromZero);
        float v = (float)d;

        var addr = HmiPlcConfigService.TryGetModbusAddress(HmiPlcConfigService.KeyYPosSet);
        if (string.IsNullOrEmpty(addr))
        {
            MessageBox.Show("数据库 config 中缺少 Addr_YPos_Set 的地址配置。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _modbus!.Write_Hmi(addr, v);
       
    }

    private void BtnOutXExec_Click(object sender, RoutedEventArgs e)
    {
        if (ChkManualTestEnable.IsChecked != true)
            return;

        if (!TryParseTargetFloat(TxtOutTargetX, "出片台X轴目标位置", out var value))
            return;

        TryWriteFloatByKey(HmiPlcConfigService.KeyOutXSet, value, "出片台X轴目标位置");
    }

    private void BtnModeAuto_Click(object sender, RoutedEventArgs e)
    {
        BtnModeAuto.Background = BrushActive;
        BtnModeManual.Background = BrushInactive;
    }

    private void BtnModeManual_Click(object sender, RoutedEventArgs e)
    {
        BtnModeManual.Background = BrushActive;
        BtnModeAuto.Background = BrushInactive;
    }

    private void BtnStationMeasure_Click(object sender, RoutedEventArgs e) => SetActiveStation(BtnStationMeasure);

    private void BtnStationCageA_Click(object sender, RoutedEventArgs e) => SetActiveStation(BtnStationCageA);

    private void BtnStationCageB_Click(object sender, RoutedEventArgs e) => SetActiveStation(BtnStationCageB);

    private void BtnStationAToB_Click(object sender, RoutedEventArgs e) => SetActiveStation(BtnStationAToB);

    private void BtnStationOut_Click(object sender, RoutedEventArgs e) => SetActiveStation(BtnStationOut);

    private void SetActiveStation(Button active)
    {
        foreach (var b in new[] { BtnStationMeasure, BtnStationCageA, BtnStationCageB, BtnStationAToB, BtnStationOut })
            b.Background = ReferenceEquals(b, active) ? BrushActive : BrushInactive;

        PnlStationMeasure.Visibility = ReferenceEquals(active, BtnStationMeasure) ? Visibility.Visible : Visibility.Collapsed;
        PnlStationCageA.Visibility = ReferenceEquals(active, BtnStationCageA) ? Visibility.Visible : Visibility.Collapsed;
        PnlStationCageB.Visibility = ReferenceEquals(active, BtnStationCageB) ? Visibility.Visible : Visibility.Collapsed;
        PnlStationOut.Visibility = ReferenceEquals(active, BtnStationOut) ? Visibility.Visible : Visibility.Collapsed;
        var other = ReferenceEquals(active, BtnStationAToB);
        PnlStationOther.Visibility = other ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChkCageA_YOriginEnable_Changed(object sender, RoutedEventArgs e) => RefreshCageAYOriginSetEnabled();

    private void ChkCageB_YOriginEnable_Changed(object sender, RoutedEventArgs e) => RefreshCageBYOriginSetEnabled();

    // ---------- A 笼：按钮功能暂定，各自独立事件（勿与 B 笼共用） ----------
    private void BtnCageA_YTargetExec_Click(object sender, RoutedEventArgs e)
    {
        if (ChkManualTestEnable.IsChecked != true)
            return;

        if (!TryParseTargetFloat(TxtCageA_YTarget, "A笼目标Y轴位置", out var value))
            return;

        TryWriteFloatByKey(HmiPlcConfigService.KeyCageAYSet, value, "A笼目标Y轴位置");
    }

    private void BtnCageA_AisleTargetExec_Click(object sender, RoutedEventArgs e)
    {
        if (ChkManualTestEnable.IsChecked != true)
            return;

        if (!TryParseAisleNo(TxtCageA_AisleTarget, "A笼目标巷道号", out var aisleNo))
            return;

        if (!TryGetAisleCoordinate("A", aisleNo, out var coordinate))
        {
            MessageBox.Show("未找到 A笼 对应巷道号的巷道位置。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TxtCageA_YTarget.Text = coordinate.ToString("F2", CultureInfo.InvariantCulture);
        TryWriteFloatByKey(HmiPlcConfigService.KeyCageAYSet, coordinate, "A笼目标巷道位置");
    }

    private void BtnCageA_LiftUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageALiftUp);
    }

    private void BtnCageA_LiftDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageALiftDown);
    }

    private void BtnCageA_XMinus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageAXMinus);
    }

    private void BtnCageA_XPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageAXPlus);
    }

    private void BtnCageA_XTargetExec_Click(object sender, RoutedEventArgs e)
    {
        if (ChkManualTestEnable.IsChecked != true)
            return;

        if (!TryParseTargetFloat(TxtCageA_XTarget, "A笼目标X轴位置", out var value))
            return;

        TryWriteFloatByKey(HmiPlcConfigService.KeyCageAXSet, value, "A笼目标X轴位置");
    }

    private void BtnCageA_YPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageAYPlus);
    }

    private void BtnCageA_YMinus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageAYMinus);
    }

    private void BtnCageA_YOriginSet_Click(object sender, RoutedEventArgs e) { }

    // ---------- B 笼：按钮功能暂定，各自独立事件（勿与 A 笼共用） ----------
    private void BtnCageB_YTargetExec_Click(object sender, RoutedEventArgs e)
    {
        if (ChkManualTestEnable.IsChecked != true)
            return;

        if (!TryParseTargetFloat(TxtCageB_YTarget, "B笼目标Y轴位置", out var value))
            return;

        TryWriteFloatByKey(HmiPlcConfigService.KeyCageBYSet, value, "B笼目标Y轴位置");
    }

    private void BtnCageB_AisleTargetExec_Click(object sender, RoutedEventArgs e)
    {
        if (ChkManualTestEnable.IsChecked != true)
            return;

        if (!TryParseAisleNo(TxtCageB_AisleTarget, "B笼目标巷道号", out var aisleNo))
            return;

        if (!TryGetAisleCoordinate("B", aisleNo, out var coordinate))
        {
            MessageBox.Show("未找到 B笼 对应巷道号的巷道位置。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TxtCageB_YTarget.Text = coordinate.ToString("F2", CultureInfo.InvariantCulture);
        TryWriteFloatByKey(HmiPlcConfigService.KeyCageBYSet, coordinate, "B笼目标巷道位置");
    }

    private void BtnCageB_LiftUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageBLiftUp);
    }

    private void BtnCageB_LiftDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageBLiftDown);
    }

    private void BtnCageB_XMinus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageBXMinus);
    }

    private void BtnCageB_XPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageBXPlus);
    }

    private void BtnCageB_XTargetExec_Click(object sender, RoutedEventArgs e)
    {
        if ( ChkManualTestEnable.IsChecked != true)
            return;

        if (!TryParseTargetFloat(TxtCageB_XTarget, "B笼目标X轴位置", out var value))
            return;

        TryWriteFloatByKey(HmiPlcConfigService.KeyCageBXSet, value, "B笼目标X轴位置");
    }

    private void BtnCageB_YPlus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageBYPlus);
    }

    private void BtnCageB_YMinus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
            StartJog(btn, HmiPlcConfigService.KeyCageBYMinus);
    }

    private void BtnCageB_YOriginSet_Click(object sender, RoutedEventArgs e) { }
}
