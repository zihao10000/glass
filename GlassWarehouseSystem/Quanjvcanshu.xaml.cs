using GlassWarehouseSystem.ViewModels;
using Microsoft.EntityFrameworkCore;
using System;
using System.Windows;
using System.Windows.Controls;
namespace GlassWarehouseSystem;
using GlassWarehouseSystem.Models;
using GlassWarehouseSystem.Services;
using GlassWarehouseSystem.LocalConfig;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.Core.IMessage;
using HslCommunication.ModBus;
using Microsoft.Win32; // 用于 OpenFileDialog
using MiniExcelLibs;   // 用于解析 Excel
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input; // 记得引用这个
using System.Windows.Media.Animation;
using HslDataFormat = HslCommunication.Core.DataFormat;

public partial class Quanjvcanshu : Window
    {
   
    private QuanjvcanshuViewModel vm;
    // --- 必须加上这一行声明，报错才会消失 ---
    private HslCommunication.ModBus.ModbusTcpNet _modbus;

    private System.Windows.Threading.DispatcherTimer _timer; // 计时器对象

    public Quanjvcanshu()
        {
            InitializeComponent();
            vm = new QuanjvcanshuViewModel();
            this.DataContext = vm; // 绑定整个 VM

        // 窗口加载时自动刷新数据
        CacheQueryService.ClearCacheByPattern("config:all_data");
        this.Loaded += async (s, e) => await vm.RefreshDataAsync();
        // 绑定加载事件
        //this.Loaded += Quanjvcanshu_Loaded;
        // --- 订阅窗口关闭事件 ---
        this.Closed += Quanjvcanshu_Closed;
        _isAdmin = false; // 初始只读
        ApplyPermission(); // 调用总闸，让按钮变灰
    }
    private bool _isAdmin = false; // 权限状态：默认不准动
 //窗口关闭释放资源
    private void Quanjvcanshu_Closed(object sender, EventArgs e)
    {
        try
        {
            // 1. 停止计时器
            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }

            // 2. 断开 PLC 连接
            if (_modbus != null)
            {
                _modbus.ConnectClose(); // HslCommunication 的断开方法
                _modbus = null;
                System.Diagnostics.Debug.WriteLine("窗口已关闭，PLC 连接已释放");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("关闭窗口时释放资源失败: " + ex.Message);
        }
    }
    // 核心方法：像“总闸”一样控制所有按钮
    private void ApplyPermission()
    {
        // 只要 _isAdmin 是 false，IsEnabled 就会变成 false (按钮自动变暗)
        Btnjiaozhun.IsEnabled = _isAdmin;
        BtnAdd.IsEnabled = _isAdmin;
        BtnSave.IsEnabled = _isAdmin;
        BtnDelete.IsEnabled = _isAdmin;
        BtnSaveAll.IsEnabled = _isAdmin;
        BtnImport.IsEnabled = _isAdmin;

        // 登录按钮文字切换，给用户反馈
        BtnAdminLogin.Content = _isAdmin ? "🔓 退出登录" : "🔒 管理员登录";
        BtnAdminLogin.Background = _isAdmin ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.Green;
    }

    // “管理员登录”按钮点击事件
    // “管理员登录”按钮点击事件
    private void BtnAdminLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_isAdmin)
        {
            // 1. 如果当前是管理员，执行退出逻辑
            _isAdmin = false;
            ApplyPermission(); // 按钮变灰
            MessageBox.Show("已退出管理员模式，操作已锁定。");
        }
        else
        {
            // 2. 实例化你改名后的窗口 ControlPlcUser1
            // 显式指定全名防止任何潜在冲突
            GlassWarehouseSystem.ControlPlcUser1 loginWin = new GlassWarehouseSystem.ControlPlcUser1();

            // 3. 以模态对话框显示
            if (loginWin.ShowDialog() == true)
            {
                // 4. 只有当登录窗口里执行了 this.DialogResult = true 才会进入这里
                _isAdmin = true;
                ApplyPermission(); // 调用总闸，点亮所有按钮（变彩色）
                MessageBox.Show("验证通过，管理员权限已开启！", "系统提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    // 1. 修改返回类型为 Task<bool>，方便主程序判断是否连上
    // 1. 修改返回类型为 Task<bool>，方便主程序判断是否连上
    private async Task<bool> ConnectPlcAsync()
    {
        // --- 1. 核心改进：不仅判断 null，还要判断连接是否依然有效 ---
        // HslCommunication 的大部分驱动都有类似的状态判断，或者直接尝试连接
        if (_modbus != null)
        {
            // 建议增加一个简单的读取测试或状态心跳判断
            // 如果你希望每次都重新从数据库读取配置，这里应该把 _modbus 销毁重来
            return true;
        }

        string ip = "192.168.1.8";
        int port = 502;
        byte station = 1;
        string formatStr = "CDAB";
        bool isZeroStart = false;

        // var plc = AppConfig.GetPrimaryPlc();
        // if (plc != null)
        // {
        //     ip = string.IsNullOrWhiteSpace(plc.PlcIp) ? ip : plc.PlcIp.Trim();
        //     station = (byte)Math.Clamp(plc.Id <= 0 ? 1 : plc.Id, 1, 255);
        // }

        try
        {
            using (var db = new GlassWarehouseSystem.Data.WarehouseDbContext())
            {
                var configs = await db.ConfigRows.AsNoTracking().ToListAsync();
                // 从 configs 中读取 IP 地址
                var ipCfg = configs.FirstOrDefault(c => c.KeyName == "PlcIp");
                if (ipCfg != null && !string.IsNullOrWhiteSpace(ipCfg.Value))
                {
                    ip = ipCfg.Value; // ✅ 正确：取 .Value（string 类型）
                }

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



            // --- 2. 异步连接 ---
            var result = await Task.Run(() => _modbus.ConnectServer());

            if (result.IsSuccess)
            {
                // --- 3. 计时器逻辑 (保持原样，只增加 UI 线程保护) ---
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

                return true; // 连接成功
            }
            else
            {
                _modbus = null;
                return false; // 连接失败
            }
        }
        catch (Exception ex)
        {
            _modbus = null;
            System.Diagnostics.Debug.WriteLine("连接异常: " + ex.Message);
            return false;
        }
    }
    // 辅助方法：将字符串 "CDAB" 转换为 Hsl 的枚举类型
    private HslCommunication.Core.DataFormat GetFormatFromString(string format)
    {
        return format.ToUpper() switch
        {
            "ABCD" => HslCommunication.Core.DataFormat.ABCD,
            "BADC" => HslCommunication.Core.DataFormat.BADC,
            "DCBA" => HslCommunication.Core.DataFormat.DCBA,
            _ => HslCommunication.Core.DataFormat.CDAB, // 默认 CDAB
        };
    }

    private void IODataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 如果没有任何行被选中（所有勾选都取消了）
        if (IODataGrid.SelectedItems.Count == 0)
        {
            ResetUI(); // 清空上方表单
            return;
        }

        // 填充数据：取最后一个点击变蓝的行
        if (IODataGrid.SelectedItems.Count > 0)
        {
            // ✅ 1. 修正类型为 ConfigRow
            var selected = IODataGrid.SelectedItems[IODataGrid.SelectedItems.Count - 1] as ConfigRow;

            if (selected != null)
            {
                txtConfigID.Text = selected.ConfigID.ToString();

                // 遍历 ComboBox 的所有选项，查找 Content 匹配的那一项
                foreach (ComboBoxItem item in cmbType.Items)
                {
                    // ✅ 2. 增强鲁棒性：防止 item.Content 或 selected.Type 为 null 时崩溃
                    string itemContent = item?.Content?.ToString();
                    if (string.Equals(itemContent, selected.Type, StringComparison.OrdinalIgnoreCase))
                    {
                        cmbType.SelectedItem = item;
                        break;
                    }
                }

                // 2. 设置“数据类型” (x:Name="Type" 这个 ComboBox)
                SetComboBoxSelectedContent(this.cmb2Type, selected.Type2);

                // ✅ 3. 填充其他字段（已全面适配 ConfigRow 模型的属性）
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
    // 统一的 UI 重置方法：清空选择，清空表单
    private void ResetUI()
    {
        IODataGrid.SelectedItems.Clear(); // 移除列表蓝色高亮
        ClearForm2();                    // 清空上方所有 TextBox
    }
    private void ClearForm2()
    {
        txtConfigID.Text = "（保存后自动生成）";
        //txtType.Clear();
        txtKeyName.Clear();
        txtValue.Clear();
        txtAddress.Clear();
        txtDescribe.Clear();
        dpUpdateTime.SelectedDate = null;
    }
    private async void BtnCalibrate_Click(object sender, RoutedEventArgs e)
    {
        Button targetBtn = sender as Button;
        if (targetBtn != null) targetBtn.IsEnabled = false; // 1. 禁用按钮防止连点

        try
        {
            // 2. 清理内存缓存并刷新当前UI视图
            CacheQueryService.ClearCacheByPattern("config:all_data");
            if (vm != null) await vm.RefreshDataAsync().ConfigureAwait(true); // 保持在 UI 线程刷新

            // 3. 确保 PLC 网络连接正常
            await ConnectPlcAsync().ConfigureAwait(true);

            if (_modbus == null)
            {
                MessageBox.Show("无法连接到 PLC，请检查网络设置。", "通信失败");
                return;
            }

            // 4. 彻底按死定时器，独占硬件通道，防止中途插队
            _timer?.Stop();

            // 5. 从数据库获取最新的“全局参数”配置
            List<ConfigRow> dbGlobalItems;
            using (var db = new Data.WarehouseDbContext())
            {
                dbGlobalItems = await db.ConfigRows
                    .Where(x => x.Type2 == "全局参数" && x.Address != null)
                    .ToListAsync().ConfigureAwait(true);
            }

            if (dbGlobalItems == null || !dbGlobalItems.Any())
            {
                MessageBox.Show("数据库中没有配置任何“全局参数”，无需校准。");
                return;
            }

            // 6. 全量推给后台线程池（UI 保持绝对流畅）
            var calibrationResult = await Task.Run(async () =>
            {
                List<string> diffDetails = new List<string>();
                List<ConfigRow> mismatchItems = new List<ConfigRow>();
                int readErrorCount = 0;

                foreach (var item in dbGlobalItems)
                {
                    var currentItem = item; // 💡 牢牢锁定当前行
                    if (currentItem?.Address == null || string.IsNullOrWhiteSpace(currentItem.Type)) continue;

                    string addrStr = currentItem.Address.ToString().Trim();
                    string dataType = currentItem.Type.ToLower().Trim();
                    string dbRawValue = currentItem.Value?.Trim() ?? "";

                    bool isReadSuccess = false;
                    string plcValueStr = "";
                    bool isMismatch = false;

                    try
                    {
                        switch (dataType)
                        {
                            case "bool":
                                var rBool = await _modbus.ReadBoolAsync(addrStr).ConfigureAwait(false);
                                if (rBool.IsSuccess)
                                {
                                    isReadSuccess = true;
                                    plcValueStr = rBool.Content.ToString();
                                    bool dbBool = (dbRawValue == "1" || dbRawValue.ToLower() == "true");
                                    isMismatch = (dbBool != rBool.Content);
                                }
                                break;

                            case "short":
                                var rShort = await _modbus.ReadInt16Async(addrStr).ConfigureAwait(false);
                                if (rShort.IsSuccess)
                                {
                                    isReadSuccess = true;
                                    plcValueStr = rShort.Content.ToString();
                                    if (short.TryParse(dbRawValue, out short dbShort))
                                        isMismatch = (dbShort != rShort.Content);
                                    else
                                        isMismatch = true; // 数据库格式错误直接标记为不一致，触发后续覆盖
                                }
                                break;

                            case "int":
                                var rInt = await _modbus.ReadInt32Async(addrStr).ConfigureAwait(false);
                                if (rInt.IsSuccess)
                                {
                                    isReadSuccess = true;
                                    plcValueStr = rInt.Content.ToString();
                                    if (int.TryParse(dbRawValue, out int dbInt))
                                        isMismatch = (dbInt != rInt.Content);
                                    else
                                        isMismatch = true;
                                }
                                break;

                            case "float":
                                var rFloat = await _modbus.ReadFloatAsync(addrStr).ConfigureAwait(false);
                                if (rFloat.IsSuccess)
                                {
                                    isReadSuccess = true;
                                    plcValueStr = rFloat.Content.ToString("F3");
                                    if (float.TryParse(dbRawValue, out float dbFloat))
                                        isMismatch = (Math.Abs(dbFloat - rFloat.Content) > 0.001); // 提高精密仪器的工业精度至 0.001
                                    else
                                        isMismatch = true;
                                }
                                break;

                            case "double":
                                var rDouble = await _modbus.ReadDoubleAsync(addrStr).ConfigureAwait(false);
                                if (rDouble.IsSuccess)
                                {
                                    isReadSuccess = true;
                                    plcValueStr = rDouble.Content.ToString("F3");
                                    if (double.TryParse(dbRawValue, out double dbDouble))
                                        isMismatch = (Math.Abs(dbDouble - rDouble.Content) > 0.001);
                                    else
                                        ;
                                }
                                break;

                            case "string":
                                var rString = await _modbus.ReadStringAsync(addrStr, 10).ConfigureAwait(false);
                                if (rString.IsSuccess)
                                {
                                    isReadSuccess = true;
                                    plcValueStr = rString.Content?.Replace("\0", "").Trim() ?? "";
                                    // 规避 Null 与 Empty 不等造成的死循环校准
                                    isMismatch = !string.Equals(dbRawValue, plcValueStr, StringComparison.OrdinalIgnoreCase);
                                }
                                break;

                            default:
                                continue;
                        }

                        // 纯后台线程挂起
                        await Task.Delay(20).ConfigureAwait(false);

                        if (isReadSuccess)
                        {
                            if (isMismatch)
                            {
                                mismatchItems.Add(currentItem);
                                diffDetails.Add($"地址 [{currentItem.Address}] ({currentItem.KeyName ?? "未命名"}): PLC当前[{plcValueStr}] -> 库中值[{dbRawValue}]");
                            }
                        }
                        else
                        {
                            readErrorCount++;
                        }
                    }
                    catch
                    {
                        readErrorCount++; // 完美隔离单个变频器/仪表断电导致的断路异常
                    }
                }

                return (mismatchItems, diffDetails, readErrorCount);
            }).ConfigureAwait(true); // 此处安全重回 UI 线程

            // 7. 自动重回 UI 线程，安全弹窗
            var (mismatchList, details, errorCount) = calibrationResult;

            if (mismatchList.Count == 0 && errorCount == 0)
            {
                MessageBox.Show("数据库和 PLC 中的全局参数数据完全一致！", "校准成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                string message = "对比完成，发现以下数据差异：\n";
                message += "--------------------------------------\n";
                message += string.Join("\n", details.Take(12));
                if (details.Count > 12) message += $"\n... 以及其他 {details.Count - 12} 项差异";
                message += "\n--------------------------------------\n";
                message += $"\n总结：共有 {mismatchList.Count} 项数值不一致，{errorCount} 项地址读取失败。";
                message += "\n\n是否确认按照【数据库的值】，全部覆盖并写入 PLC 硬件？";

                var result = MessageBox.Show(message, "全量数据校准确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    // 8. 全量覆盖反写
                    int successCount = await Task.Run(async () =>
                    {
                        int currentSuccess = 0;
                        foreach (var item in mismatchList)
                        {
                            var currentWriteItem = item; // 💡 牢牢锁定当前写入行
                            if (currentWriteItem?.Address == null) continue;

                            string addrStr = currentWriteItem.Address.ToString().Trim();
                            string valStr = currentWriteItem.Value;
                            string dataType = currentWriteItem.Type?.ToLower()?.Trim();
                            HslCommunication.OperateResult writeRes = null;

                            try
                            {
                                string cleanVal = valStr?.Trim() ?? "";

                                switch (dataType)
                                {
                                    case "bool":
                                        bool bVal = (cleanVal == "1" || cleanVal.ToLower() == "true");
                                        writeRes = await _modbus.WriteAsync(addrStr, bVal).ConfigureAwait(false);
                                        break;
                                    case "short":
                                        if (short.TryParse(cleanVal, out short s))
                                            writeRes = await _modbus.WriteAsync(addrStr, s).ConfigureAwait(false);
                                        break;
                                    case "int":
                                        if (int.TryParse(cleanVal, out int i))
                                            writeRes = await _modbus.WriteAsync(addrStr, i).ConfigureAwait(false);
                                        break;
                                    case "float":
                                        if (float.TryParse(cleanVal, out float f))
                                            writeRes = await _modbus.WriteAsync(addrStr, f).ConfigureAwait(false);
                                        break;
                                    case "double":
                                        if (double.TryParse(cleanVal, out double d))
                                            writeRes = await _modbus.WriteAsync(addrStr, d).ConfigureAwait(false);
                                        break;
                                    case "string":
                                        writeRes = await _modbus.WriteAsync(addrStr, cleanVal, 10).ConfigureAwait(false);
                                        break;
                                    default:
                                        if (float.TryParse(cleanVal, out float df))
                                            writeRes = await _modbus.WriteAsync(addrStr, df).ConfigureAwait(false);
                                        break;
                                }

                                // 25ms 延时保护硬件接收缓存区
                                await Task.Delay(25).ConfigureAwait(false);
                                if (writeRes != null && writeRes.IsSuccess) currentSuccess++;
                            }
                            catch { /* 阻断单点故障，确保大部队继续反写 */ }
                        }
                        return currentSuccess;
                    }).ConfigureAwait(true); // 重回 UI 线程

                    MessageBox.Show($"强制同步完成！成功纠正并覆盖 {successCount} 条差异数据到 PLC。", "同步结果");
                }
            }
        }
        catch (Exception ex)
        {
            // 顶层异常壁垒，防止 async void 崩溃击穿上位机程序
            MessageBox.Show("校准执行过程中发生关键异常：" + ex.Message, "系统错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // 9. 后置收尾：绝对安全的资源恢复
            try
            {
                CacheQueryService.ClearCacheByPattern("config:all_data");
                if (vm != null) await vm.RefreshDataAsync().ConfigureAwait(true);
            }
            catch { /* 隔绝UI刷新波动的异常 */ }

            // 最后的防线：确保即便发生物理断网，定时器和按钮也能重新解锁
            try
            {
                _timer?.Start();
            }
            catch { /* 防止 Timer 内部启动冲突报错 */ }

            if (targetBtn != null) targetBtn.IsEnabled = true;
        }
    }
    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        CacheQueryService.ClearCacheByPattern("config:all_data");

        // 1. 创建新对象（✅ 已将 Config 改为 ConfigRow）
        var newConfig = new ConfigRow
        {
            ConfigID = 0,
            Type = (cmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? cmbType.Text,
            Type2 = (cmb2Type.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? cmb2Type.Text,
            KeyName = txtKeyName.Text,
            Value = txtValue.Text,
            Describe = txtDescribe.Text,
            UpdateTime = DateTime.Now
        };
        if (int.TryParse(txtAddress.Text, out int addr)) newConfig.Address = addr;

        // 2. 加入列表
        vm.ConfigList.Add(newConfig);

        // 3. 将新行加入到选中集合中，而不清除旧的选中
        IODataGrid.SelectedItems.Add(newConfig);

        // 4. 定位（依然滚动到最新的一行）
        IODataGrid.UpdateLayout();
        IODataGrid.ScrollIntoView(newConfig);

        // 5. 清空表单
        txtKeyName.Clear();
        txtValue.Clear();
        txtAddress.Clear();
        txtDescribe.Clear();
        cmbType.Focus();
    }

    private async void BtnSaveToPLC_Click(object sender, RoutedEventArgs e)
    {
        CacheQueryService.ClearCacheByPattern("config:all_data");

        Button targetBtn = sender as Button;
        // 禁用按钮防止连点
        if (targetBtn != null) targetBtn.IsEnabled = false;

        try
        {
            // 1. 获取选中项快照
            var selectedItems = IODataGrid.SelectedItems.Cast<ConfigRow>().ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("请先选中要保存的行！");
                return;
            }

            // 预定义汇总变量
            int dbSuccess = 0;
            int plcSuccess = 0;
            List<string> errorMsgs = new List<string>();
            List<string> skippedItems = new List<string>();
            List<string> addedItems = new List<string>();

            // 定义待移除记录列表
            List<ConfigRow> itemsToRemove = new List<ConfigRow>();

            // 检查本次选中的项里面，到底有没有全局参数
            bool hasGlobalParams = selectedItems.Any(item => item.Type2 == "全局参数");

            if (hasGlobalParams)
            {
                // 只有存在全局参数时，才在循环前连接一次 PLC，并彻底暂停定时器
                await ConnectPlcAsync();
                _timer?.Stop();
            }

            using (var db = new Data.WarehouseDbContext())
            {
                // 🚩 大循环开始：处理所有行
                foreach (var item in selectedItems)
                {
                    // ================== ① 全局参数分支 ==================
                    if (item.Type2 == "全局参数")
                    {
                        // 检查地址是否配置
                        if (item.Address == null)
                        {
                            errorMsgs.Add($"[数据错误] {item.KeyName}: 全局参数必须配置 PLC 地址。");
                            continue;
                        }

                        if (_modbus == null)
                        {
                            errorMsgs.Add($"[通信失败] {item.KeyName}: PLC 连接未建立，无法写入寄存器。");
                            continue;
                        }

                        // 数据库查重（无键实体只能用来查询，这里是没问题的）
                        var exist = await db.ConfigRows.FirstOrDefaultAsync(c =>
                            c.Address == item.Address &&
                            c.Type2 == "全局参数");

                        if (exist != null)
                        {
                            skippedItems.Add($"[全局参数]{item.KeyName}地址[{item.Address}]值({item.Value}) - 冲突！数据库已存在值: [{exist.Value}]");
                            itemsToRemove.Add(item);
                            continue;
                        }

                        // 写入 PLC
                        HslCommunication.OperateResult writeRes = null;
                        string addrStr = item.Address.ToString();
                        string valStr = item.Value;
                        string dataType = item.Type?.ToLower();

                        try
                        {
                            string cleanVal = valStr?.Trim();

                            switch (dataType)
                            {
                                case "bool":
                                    bool bVal = (cleanVal == "1" || cleanVal?.ToLower() == "true");
                                    writeRes = await _modbus.WriteAsync(addrStr, bVal);
                                    break;

                                case "short":
                                    if (short.TryParse(cleanVal, out short s))
                                        writeRes = await _modbus.WriteAsync(addrStr, s);
                                    else
                                        throw new Exception($"[{cleanVal}] 不是有效的 16位整数(short)");
                                    break;

                                case "int":
                                    if (int.TryParse(cleanVal, out int i))
                                        writeRes = await _modbus.WriteAsync(addrStr, i);
                                    else
                                        throw new Exception($"[{cleanVal}] 不是有效的 32位整数(int)");
                                    break;

                                case "float":
                                    if (float.TryParse(cleanVal, out float f))
                                        writeRes = await _modbus.WriteAsync(addrStr, f);
                                    else
                                        throw new Exception($"[{cleanVal}] 不是有效的 小数(float)");
                                    break;

                                case "double":
                                    if (double.TryParse(cleanVal, out double d))
                                        writeRes = await _modbus.WriteAsync(addrStr, d);
                                    else
                                        throw new Exception($"[{cleanVal}] 不是有效的 双精度小数(double)");
                                    break;

                                case "string":
                                    writeRes = await _modbus.WriteAsync(addrStr, cleanVal, 10);
                                    break;

                                default:
                                    if (float.TryParse(cleanVal, out float df))
                                        writeRes = await _modbus.WriteAsync(addrStr, df);
                                    else
                                        throw new Exception("未知数据类型且格式非法");
                                    break;
                            }

                            if (writeRes != null && writeRes.IsSuccess)
                            {
                                plcSuccess++;
                                await Task.Delay(30); // 适度硬件缓冲延迟

                                DateTime now = DateTime.Now;

                                // ✅ 关键改动：绕过 EF 追踪，使用原生 SQL 直接插入底层 MySQL 数据库
                                string sql = "INSERT INTO config (Type, Type2, KeyName, Value, Address, `Describe`, UpdateTime) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})";
                                await db.Database.ExecuteSqlRawAsync(sql, item.Type, item.Type2, item.KeyName, item.Value, item.Address, item.Describe, now);

                                addedItems.Add($"[全局参数] {item.KeyName} - 地址[{item.Address}] 写入并新增成功");
                                dbSuccess++;
                            }
                            else
                            {
                                errorMsgs.Add($"[PLC][全局参数] {item.KeyName} 写入失败: {(writeRes?.Message ?? "无响应")}");
                            }
                        }
                        catch (Exception ex)
                        {
                            errorMsgs.Add($"[数据错误] {item.KeyName}({valStr}) 转换或写入异常: {ex.Message}");
                        }
                    }
                    // ================== ② 软件参数处理 ==================
                    else if (item.Type2 == "软件参数")
                    {
                        try
                        {
                            var exist = await db.ConfigRows.FirstOrDefaultAsync(c =>
                                c.KeyName == item.KeyName &&
                                c.Type2 == "软件参数");

                            if (exist != null)
                            {
                                skippedItems.Add($"[软件参数]存在名称{item.KeyName} - 冲突！");
                                itemsToRemove.Add(item);
                                continue;
                            }
                            else
                            {
                                DateTime now = DateTime.Now;

                                // ✅ 关键改动：使用原生 SQL 插入
                                string sql = "INSERT INTO config (Type, Type2, KeyName, Value, Address, `Describe`, UpdateTime) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})";
                                await db.Database.ExecuteSqlRawAsync(sql, item.Type, item.Type2, item.KeyName, item.Value, item.Address, item.Describe, now);

                                dbSuccess++;
                                addedItems.Add($"[软件参数] {item.KeyName} - 保存成功");
                            }
                        }
                        catch (Exception ex)
                        {
                            errorMsgs.Add($"[软件参数] {item.KeyName} 保存异常: {ex.Message}");
                        }
                    }
                    // ================== ③ 实时参数处理 ==================
                    else if (item.Type2 == "实时参数")
                    {
                        try
                        {
                            var exist = await db.ConfigRows.FirstOrDefaultAsync(c =>
                                c.KeyName == item.KeyName &&
                                c.Type2 == "实时参数");

                            if (exist != null)
                            {
                                skippedItems.Add($"[实时参数]存在名称{item.KeyName} - 冲突！");
                                itemsToRemove.Add(item);
                                continue;
                            }
                            else
                            {
                                DateTime now = DateTime.Now;

                                // ✅ 关键改动：使用原生 SQL 插入
                                string sql = "INSERT INTO config (Type, Type2, KeyName, Value, Address, `Describe`, UpdateTime) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})";
                                await db.Database.ExecuteSqlRawAsync(sql, item.Type, item.Type2, item.KeyName, item.Value, item.Address, item.Describe, now);

                                dbSuccess++;
                                addedItems.Add($"[实时参数] {item.KeyName} - 保存成功");
                            }
                        }
                        catch (Exception ex)
                        {
                            errorMsgs.Add($"[实时参数] {item.KeyName} 保存异常: {ex.Message}");
                        }
                    }
                }

                // 💡 因为上面全部改为 ExecuteSqlRawAsync 实时写入了，
                // 这里的 db.SaveChangesAsync() 不再需要，直接移除或留空即可。
            }

            // 从内存列表中移除冲突的项
            foreach (var itemToDelete in itemsToRemove)
            {
                vm.ConfigList.Remove(itemToDelete);
            }

            // 构建汇报内容
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("========= 任务执行汇总报告 =========");

            if (addedItems.Any())
            {
                sb.AppendLine("\n【✅ 成功列表】:");
                sb.AppendLine(string.Join("\n", addedItems));
            }
            if (skippedItems.Any())
            {
                sb.AppendLine("\n【🚫 跳过列表(冲突)】:");
                sb.AppendLine(string.Join("\n", skippedItems));
            }
            if (errorMsgs.Any())
            {
                sb.AppendLine("\n【❌ 错误/跳过列表】:");
                sb.AppendLine(string.Join("\n", errorMsgs));
            }

            sb.AppendLine("\n------------------------------------");
            sb.AppendLine($"统计报告：- 数据库成功操作：{dbSuccess} 条 / - PLC 成功写入：{plcSuccess} 条");

            MessageBox.Show(sb.ToString(), "批处理完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("执行过程中发生系统级异常：" + ex.Message);
        }
        finally
        {
            // --- 最终恢复状态与刷新界面 ---
            CacheQueryService.ClearCacheByPattern("config:all_data");
            if (vm != null) await vm.RefreshDataAsync();

            IODataGrid.SelectedItems.Clear();
            _timer?.Start();

            if (targetBtn != null) targetBtn.IsEnabled = true;
        }
    }

    //// 全部保存：提交所有更改并清除蓝色
    //private async void BtnSaveAll_Click(object sender, RoutedEventArgs e)
    //{
    //    BtnSaveAll.IsEnabled = false;
    //    await vm.SaveAllAsync();
    //    IODataGrid.SelectedItems.Clear(); // 只有保存了，才取消蓝色高亮
    //    BtnSaveAll.IsEnabled = true;
    //    MessageBox.Show("所有数据已同步（已更新 Redis）");
    //}

    // 保存按钮：仅保存当前选中的那一行
    // 1. 修正后的同步函数（确保类型对齐）


    // 1. 这是你原本的按钮事件，现在改成了通用的异步数据库保存功能函数
    private async Task SaveSelectedItemsAsync()
    {
        // 1. 获取所有选中的行（✅ 已将 Config 改为 ConfigRow）
        var selectedItems = IODataGrid.SelectedItems.Cast<ConfigRow>().ToList();

        if (selectedItems.Count == 0)
        {
            MessageBox.Show("请先在列表中选中要保存的行（支持多选）。");
            return;
        }

        try
        {
            using (var db = new Data.WarehouseDbContext())
            {
                foreach (var item in selectedItems)
                {
                    item.UpdateTime = DateTime.Now;
                    if (item.ConfigID == 0)
                    {
                        db.ConfigRows.Add(item); // ✅ 修正为 db.ConfigRows
                    }
                    else
                    {
                        db.ConfigRows.Update(item); // ✅ 修正为 db.ConfigRows
                    }
                }

                // 一次性提交所有选中的行
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("数据库保存过程中出错：" + ex.Message);
        }
    }


    // 删除按钮：从列表和数据库中移除，并同步清理 PLC 里的内容
    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (IODataGrid.SelectedItems.Count == 0) return;

        Button targetBtn = sender as Button;
        if (targetBtn != null) targetBtn.IsEnabled = false;

        if (MessageBox.Show("确定删除选中的数据吗？如果是全局参数这会删除数据库的数据的同时清空 PLC 对应地址的值！", "提示", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            // 1. 获取选中的行列表快照
            var selectedList = IODataGrid.SelectedItems.Cast<ConfigRow>().ToList();

            try
            {
                // ================== 💡 优化一：按业务规则，只有包含全局参数时才去碰 PLC ==================
                bool hasGlobalParams = selectedList.Any(item => item.Type2 == "全局参数");

                if (hasGlobalParams)
                {
                    await ConnectPlcAsync();
                    _timer?.Stop();

                    // 如果有全局参数但是连不上 PLC，记录提示并中止，保护硬件一致性
                    if (_modbus == null)
                    {
                        MessageBox.Show("检测到要删除的数据中包含【全局参数】，但 PLC 连接未建立，操作已中止以防硬件数据不一致。", "通信提醒");
                        return;
                    }
                }

                using (var db = new Data.WarehouseDbContext())
                {
                    foreach (var item in selectedList)
                    {
                        var currentItem = item;

                        // ================== 💡 优化二：A. PLC 清零（严格限制只有全局参数才操作硬件） ==================
                        if (currentItem.Type2 == "全局参数" && currentItem.Address != null && _modbus != null)
                        {
                            string addrStr = currentItem.Address.ToString();
                            string dataType = currentItem.Type?.ToLower();
                            HslCommunication.OperateResult writeRes;

                            switch (dataType)
                            {
                                case "bool":
                                    writeRes = await _modbus.WriteAsync(addrStr, false);
                                    break;

                                case "short":
                                    writeRes = await _modbus.WriteAsync(addrStr, (short)0);
                                    break;

                                case "int":
                                    writeRes = await _modbus.WriteAsync(addrStr, 0);
                                    break;

                                case "float":
                                    writeRes = await _modbus.WriteAsync(addrStr, 0f);
                                    break;

                                case "double":
                                    writeRes = await _modbus.WriteAsync(addrStr, 0d);
                                    break;

                                case "string":
                                    writeRes = await _modbus.WriteAsync(addrStr, "", 10);
                                    break;

                                default:
                                    writeRes = await _modbus.WriteAsync(addrStr, (short)0);
                                    break;
                            }

                            // 硬件清空失败时，在后台输出或者给个非阻塞提示，不要轻易打断数据库删除
                            if (writeRes != null && !writeRes.IsSuccess)
                            {
                                System.Diagnostics.Debug.WriteLine($"[PLC警告] 清空地址 {addrStr} 失败: {writeRes.Message}");
                            }
                        }

                        // ================== 💡 优化三：B. 数据库删除（绕过 EF 追踪，改用原生 SQL 解决无键报错） ==================
                        if (currentItem.ConfigID != 0)
                        {
                            // 鉴于 config 表被映射为了 HasNoKey()，直接使用唯一标识 ConfigID 跑底层删除语句
                            string sql = "DELETE FROM config WHERE ConfigID = {0}";
                            await db.Database.ExecuteSqlRawAsync(sql, currentItem.ConfigID);
                        }
                    }

                    // 💡 因为全改为了 ExecuteSqlRawAsync 实时物理删除，不再需要 db.SaveChangesAsync()
                }

                MessageBox.Show("数据处理完成：选定记录已从数据库移除，相关 PLC 硬件地址已同步清零（如是全局参数）。", "操作成功");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"执行过程中发生异常：{ex.Message}", "系统错误");
            }
            finally
            {
                // 恢复界面与重置状态
                CacheQueryService.ClearCacheByPattern("config:all_data");
                if (vm != null) await vm.RefreshDataAsync();

                // 清除选中状态
                IODataGrid.SelectedItems.Clear();
                if (targetBtn != null) targetBtn.IsEnabled = true;
                _timer?.Start();
            }
        }
        else
        {
            // 用户点了取消，恢复按钮状态
            if (targetBtn != null) targetBtn.IsEnabled = true;
        }
    }
    private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        // 1. 检查是否有选中的行
        if (IODataGrid.SelectedItem is not ConfigRow selectedItem)
        {
            MessageBox.Show("请先在列表中选择要修改的行。");
            return;
        }

        // 额外安全校验：如果是未保存的新行（ConfigID 为 0），应该去点“保存”而不是“修改”
        if (selectedItem.ConfigID == 0)
        {
            MessageBox.Show("该行是尚未保存到数据库的新行，请使用【保存】功能。");
            return;
        }

        Button targetBtn = sender as Button;
        if (targetBtn != null) targetBtn.IsEnabled = false; // 禁用按钮防止重复点击

        try
        {
            // 💡 提取 UI 界面输入框的值到临时变量（防止提前污染内存对象导致异常时界面数据错乱）
            string newType = (cmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? cmbType.Text;
            string newType2 = (cmb2Type.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? cmb2Type.Text;
            string newKeyName = txtKeyName.Text;
            string newValue = txtValue.Text;
            string newDescribe = txtDescribe.Text;
            int? newAddress = int.TryParse(txtAddress.Text, out int addr) ? addr : null;
            DateTime now = DateTime.Now;

            // --- 2. 核心逻辑判断：如果是“全局参数”，强制同步覆盖 PLC ---
            if (newType2 == "全局参数")
            {
                if (newAddress == null)
                {
                    MessageBox.Show("错误：全局参数必须配置有效的 PLC 地址才能同步！", "同步中止");
                    return; // 触发 finally
                }

                // --- 确保长连接 ---
                await ConnectPlcAsync();

                _timer?.Stop();
                var modbus = _modbus;

                if (modbus != null)
                {
                    string addrStr = newAddress.ToString();
                    string valStr = newValue;
                    string dataType = newType?.ToLower();
                    HslCommunication.OperateResult writeRes;

                    string cleanVal = valStr?.Trim();

                    // 细化类型判断
                    switch (dataType)
                    {
                        case "bool":
                            bool bVal = (cleanVal == "1" || cleanVal?.ToLower() == "true");
                            writeRes = await _modbus.WriteAsync(addrStr, bVal);
                            break;

                        case "short":
                            if (short.TryParse(cleanVal, out short s))
                                writeRes = await _modbus.WriteAsync(addrStr, s);
                            else
                                throw new Exception($"[{cleanVal}] 不是有效的 16位整数(short)");
                            break;

                        case "int":
                            if (int.TryParse(cleanVal, out int i))
                                writeRes = await _modbus.WriteAsync(addrStr, i);
                            else
                                throw new Exception($"[{cleanVal}] 不是有效的 32位整数(int)");
                            break;

                        case "float":
                            if (float.TryParse(cleanVal, out float f))
                                writeRes = await _modbus.WriteAsync(addrStr, f);
                            else
                                throw new Exception($"[{cleanVal}] 不是有效的 小数(float)");
                            break;

                        case "double":
                            if (double.TryParse(cleanVal, out double d))
                                writeRes = await _modbus.WriteAsync(addrStr, d);
                            else
                                throw new Exception($"[{cleanVal}] 不是有效的 双精度小数(double)");
                            break;

                        case "string":
                            writeRes = await _modbus.WriteAsync(addrStr, cleanVal, 10);
                            break;

                        default:
                            if (float.TryParse(cleanVal, out float df))
                                writeRes = await _modbus.WriteAsync(addrStr, df);
                            else
                                throw new Exception("未知数据类型且格式非法");
                            break;
                    }

                    if (writeRes != null && !writeRes.IsSuccess)
                    {
                        MessageBox.Show($"PLC 写入失败：{writeRes.Message}。数据库已准备更新。", "同步警告");
                    }
                }
                else
                {
                    MessageBox.Show("PLC 连接失败！本次修改仅更新数据库，请后续手动校准。", "连接提醒");
                }
            }

            // --- 3. 统一调用数据库修改逻辑（绕过 EF 追踪，采用原生 SQL 精准更新无键实体） ---
            using (var db = new Data.WarehouseDbContext())
            {
                // 在 MySQL 中，Describe 是关键字，必须用反引号 `Describe` 包裹
                string sql = @"UPDATE config 
                           SET Type = {0}, Type2 = {1}, KeyName = {2}, Value = {3}, Address = {4}, `Describe` = {5}, UpdateTime = {6} 
                           WHERE ConfigID = {7}";

                await db.Database.ExecuteSqlRawAsync(sql, newType, newType2, newKeyName, newValue, newAddress, newDescribe, now, selectedItem.ConfigID);
            }

            // 4. 清理缓存
            CacheQueryService.ClearCacheByPattern("config:all_data");

            string tipMsg = newType2 == "全局参数" ? "修改成功（数据已同步至 PLC 和数据库）" : "修改成功（非全局参数仅能更新数据库）";
            MessageBox.Show(tipMsg, "操作完成");
        }
        catch (Exception ex)
        {
            MessageBox.Show("更新失败，请检查输入格式是否正确：" + ex.Message);
        }
        finally
        {
            // 5. 刷新界面与状态恢复
            CacheQueryService.ClearCacheByPattern("config:all_data");
            if (vm != null) await vm.RefreshDataAsync();

            // 清除选中状态
            IODataGrid.SelectedItems.Clear();

            if (targetBtn != null) targetBtn.IsEnabled = true;
            _timer?.Start();
        }
    }

    private void ClearForm()
    {
        txtKeyName.Clear();
        txtValue.Clear();
        txtAddress.Clear();
        txtDescribe.Clear();
        cmbType.Focus();
    }

    // ✅ 顺手帮你把注释掉的代码也改成新模型，防止以后解除注释报错
    private void SyncFormToObject(ConfigRow target)
    {
        target.Type = (cmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? cmbType.Text;
        target.KeyName = txtKeyName.Text;
        target.Value = txtValue.Text;
        target.Describe = txtDescribe.Text;
        if (int.TryParse(txtAddress.Text, out int addr)) target.Address = addr;
        target.UpdateTime = DateTime.Now;
    }

    private void DataGridRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 【关键点 1】检查功能键。如果是 Shift 或 Ctrl，直接 Return
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        // 【关键点 2】只有在普通点击（单选）时，才执行自定义反选逻辑
        if (sender is DataGridRow row)
        {
            bool wasSelected = row.IsSelected;

            // 清除其他选中（实现单选）
            IODataGrid.SelectedItems.Clear();

            // 反转当前行状态
            row.IsSelected = !wasSelected;

            // 拦截事件，不让 DataGrid 原生逻辑运行
            e.Handled = true;

            // 必须聚焦，否则 SelectionChanged 可能不触发
            row.Focus();
        }
    }
    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        // 1. 二次确认
        MessageBoxResult confirm = MessageBox.Show(
            "确定要导入 Excel 吗？\n系统将根据【参数名称(KeyName)】匹配：已存在的将覆盖更新，不存在的将新增。",
            "批量导入确认", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        // 获取并禁用按钮，防止处理期间重复点击
        Button targetBtn = sender as Button;
        if (targetBtn != null) targetBtn.IsEnabled = false;

        try
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Excel Files (*.xlsx;*.xls)|*.xlsx;*.xls";

            if (openFileDialog.ShowDialog() == true)
            {
                // 将 Excel 数据读取到内存
                var rows = MiniExcelLibs.MiniExcel.Query<ConfigRow>(openFileDialog.FileName).ToList();
                if (rows == null || rows.Count == 0)
                {
                    MessageBox.Show("Excel 文件中未读取到有效数据。", "提示");
                    return;
                }

                // 2. 内存先行去重：防止 Excel 内部本身就填重复了相同的 KeyName
                var distinctRows = rows
                    .Where(x => !string.IsNullOrWhiteSpace(x.KeyName))
                    .GroupBy(x => x.KeyName.Trim())
                    .Select(g => g.First())
                    .ToList();

                int addCount = 0;
                int updateCount = 0;
                int plcWriteCount = 0;
                List<string> successDetails = new List<string>();
                List<string> errorDetails = new List<string>();

                // 3. 建立 PLC 统一长连接逻辑并按死定时器
                await ConnectPlcAsync().ConfigureAwait(true);
                _timer?.Stop();

                var modbus = _modbus;
                DateTime now = DateTime.Now;

                // 4. 将高耗时的数据库和网络 I/O 整体打包推给后台线程池，彻底防止 WPF 界面白屏卡死
                await Task.Run(async () =>
                {
                    using (var db = new Data.WarehouseDbContext())
                    {
                        foreach (var excelItem in distinctRows)
                        {
                            try
                            {
                                string currentKeyName = excelItem.KeyName.Trim();

                                // 💡 核心修正：只根据唯一索引 KeyName 去查。只要 KeyName 存在，就绝对是 UPDATE，不允许 INSERT！
                                var dbExist = await db.ConfigRows
                                    .FirstOrDefaultAsync(c => c.KeyName == currentKeyName)
                                    .ConfigureAwait(false);

                                if (dbExist != null)
                                {
                                    // 存在记录 -> 执行原生 UPDATE (规避 Describe 关键字与地址变更导致的冲突)
                                    string updateSql = @"UPDATE config 
                                                     SET Type = {0}, Type2 = {1}, Value = {2}, Address = {3}, `Describe` = {4}, UpdateTime = {5} 
                                                     WHERE KeyName = {6}";

                                    await db.Database.ExecuteSqlRawAsync(updateSql,
                                        excelItem.Type, excelItem.Type2, excelItem.Value, excelItem.Address, excelItem.Describe, now,
                                        currentKeyName).ConfigureAwait(false);

                                    updateCount++;
                                }
                                else
                                {
                                    // 不存在记录 -> 执行原生 INSERT
                                    string insertSql = @"INSERT INTO config (Type, Type2, KeyName, Value, Address, `Describe`, UpdateTime) 
                                                     VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})";

                                    await db.Database.ExecuteSqlRawAsync(insertSql,
                                        excelItem.Type, excelItem.Type2, currentKeyName, excelItem.Value, excelItem.Address, excelItem.Describe, now).ConfigureAwait(false);

                                    addCount++;
                                }

                                // ================== 5. PLC 实时同步反写 ==================
                                if (excelItem.Type2 == "全局参数" && excelItem.Address != null && modbus != null)
                                {
                                    string addrStr = excelItem.Address.ToString().Trim();
                                    string valStr = excelItem.Value;
                                    string dataType = excelItem.Type?.ToLower()?.Trim();

                                    HslCommunication.OperateResult writeRes = null;
                                    string cleanVal = valStr?.Trim() ?? "";

                                    switch (dataType)
                                    {
                                        case "bool":
                                            bool bVal = (cleanVal == "1" || cleanVal.ToLower() == "true");
                                            writeRes = await modbus.WriteAsync(addrStr, bVal).ConfigureAwait(false);
                                            break;

                                        case "short":
                                            if (short.TryParse(cleanVal, out short s))
                                                writeRes = await modbus.WriteAsync(addrStr, s).ConfigureAwait(false);
                                            else
                                                throw new Exception($"[{cleanVal}] 不是有效的 16位整数(short)");
                                            break;

                                        case "int":
                                            if (int.TryParse(cleanVal, out int i))
                                                writeRes = await modbus.WriteAsync(addrStr, i).ConfigureAwait(false);
                                            else
                                                throw new Exception($"[{cleanVal}] 不是有效的 32位整数(int)");
                                            break;

                                        case "float":
                                            if (float.TryParse(cleanVal, out float f))
                                                writeRes = await modbus.WriteAsync(addrStr, f).ConfigureAwait(false);
                                            else
                                                throw new Exception($"[{cleanVal}] 不是有效的 小数(float)");
                                            break;

                                        case "double":
                                            if (double.TryParse(cleanVal, out double d))
                                                writeRes = await modbus.WriteAsync(addrStr, d).ConfigureAwait(false);
                                            else
                                                throw new Exception($"[{cleanVal}] 不是有效的 双精度小数(double)");
                                            break;

                                        case "string":
                                            writeRes = await modbus.WriteAsync(addrStr, cleanVal, 10).ConfigureAwait(false);
                                            break;

                                        default:
                                            if (float.TryParse(cleanVal, out float df))
                                                writeRes = await modbus.WriteAsync(addrStr, df).ConfigureAwait(false);
                                            break;
                                    }

                                    // 控制写入频次 25ms，保护硬件缓冲区
                                    await Task.Delay(25).ConfigureAwait(false);

                                    if (writeRes != null && writeRes.IsSuccess)
                                        plcWriteCount++;
                                    else
                                        errorDetails.Add($"[PLC写入失败] {currentKeyName}: {(writeRes?.Message ?? "通信无响应")}");
                                }

                                successDetails.Add($"[{excelItem.Type2 ?? "未知"}]- {currentKeyName} -> {excelItem.Value}");
                            }
                            catch (Exception ex)
                            {
                                errorDetails.Add($"[条目处理异常] {excelItem.KeyName}: {ex.Message}");
                            }
                        }
                    }
                }).ConfigureAwait(true); // 完美重回 UI 线程

                // 6. 汇总报告展示
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("========== 导入任务汇总报告 ==========");
                sb.AppendLine($"执行时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("------------------------------------");
                sb.AppendLine($"【统计】新增: {addCount} 条 | 更新: {updateCount} 条");
                sb.AppendLine($"【PLC 】同步成功: {plcWriteCount} 条");
                sb.AppendLine($"【总计】成功处理: {addCount + updateCount} 条");

                if (successDetails.Any())
                {
                    sb.AppendLine("\n【执行清单(前15项)】:");
                    foreach (var item in successDetails.Take(15)) sb.AppendLine(item);
                    if (successDetails.Count > 15) sb.AppendLine($"... 等共计 {successDetails.Count} 项");
                }

                if (errorDetails.Any())
                {
                    sb.AppendLine("\n【❌ 失败记录】:");
                    foreach (var err in errorDetails) sb.AppendLine(err);
                }

                MessageBox.Show(sb.ToString(), "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"严重错误: {ex.Message}", "系统异常", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // 7. 资源绝对安全恢复
            try
            {
                CacheQueryService.ClearCacheByPattern("config:all_data");
                if (vm != null) await vm.RefreshDataAsync().ConfigureAwait(true);
            }
            catch { /* 屏蔽UI刷新导致的次生异常 */ }

            if (targetBtn != null) targetBtn.IsEnabled = true;

            try
            {
                _timer?.Start();
            }
            catch { /* 防止定时器多线程冲突 */ }
        }
    }
}
