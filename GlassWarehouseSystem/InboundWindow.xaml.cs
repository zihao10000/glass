using GlassWarehouseSystem.Config;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Models;
using GlassWarehouseSystem.Repositories;
using GlassWarehouseSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using OfficeOpenXml;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace GlassWarehouseSystem;

/// <summary>
/// 入笼主界面（InboundWindow）的代码后台。
///
///   1. 展示入库计划列表（dgPlan）：从缓存或数据库加载所有非完成状态的 Material，
///      并在每次入库事务完成后通过 OnInboundStatusChanged 事件自动刷新。
///   2. 启动 / 停止入笼服务（InboundService），监听其日志、异常和比对记录事件，
///      将结果实时显示到日志区、异常列表和比对记录列表。
///   3. 提供操作按钮：手动标记破损、手动入库、顺移A→B、PLC复位、CSV导出等。
///   4. 在窗口打开时向 PLC 写入系统启动信号（Addr_SystemStart=1），
///      在窗口关闭时写入停止信号（Addr_SystemStart=0）。
/// </summary>
public partial class InboundWindow : Window
{
    /// <summary>
    /// 入库计划列表的数据源，绑定到 dgPlan.ItemsSource。
    /// 使用 ObservableCollection 以便 WPF 数据绑定在集合变化时自动刷新 UI。
    /// </summary>
    private readonly ObservableCollection<PlanItemViewModel> _planItems = new();

    /// <summary>
    /// 异常记录数据源，绑定到 dgExceptions.ItemsSource。
    /// 新产生的异常总是插入到集合头部（索引 0），保证最新异常显示在列表顶端。
    /// </summary>
    private readonly ObservableCollection<ExceptionRecordViewModel> _exceptionRecords = new();

    /// <summary>
    /// CCD（工业相机）比对记录数据源，绑定到 dgCompare.ItemsSource。
    /// 新产生的比对结果总是插入到集合头部（索引 0），保证最新记录显示在列表顶端。
    /// </summary>
    private readonly ObservableCollection<CompareRecordViewModel> _compareRecords = new();

    /// <summary>
    /// 入笼核心服务，内部维护三条后台线程：
    ///   - 安全监控线程：持续检测 PLC 传感器信号，处理急停等异常；
    ///   - 测量生产线程：调用 CCD 采集玻璃尺寸，入队等待匹配；
    ///   - 入库消费线程：从匹配队列取出结果，并驱动 PLC 完成物理入笼动作。
    /// 通过以下四个事件向 UI 推送状态变化：
    ///   - OnLogActivity：文字日志（TRACE 级别，详细 PLC 通讯步骤）；
    ///   - OnExceptionRecord：异常事件，需要在界面异常列表中显示；
    ///   - OnCompareRecord：比对完成事件，携带 CCD 数据及匹配结果；
    ///   - OnInboundStatusChanged：入库事务结束或状态变更，需要刷新计划列表。
    /// </summary>
    private readonly InboundService _inboundService = new InboundService();

    /// <summary>
    /// PLC 通讯服务，UI 层直接持有，用于以下操作：
    ///   - 系统启动时写入 Addr_SystemStart=1，通知现场设备上位机已就绪；
    ///   - 系统关闭时写入 Addr_SystemStart=0，通知现场设备上位机已离线；
    ///   - 点击"PLC 复位"按钮时清零指令地址。
    /// 注意：PlcClient.Instance 是全应用单例，InboundService、ShiftService、OutboundWindow 均共享同一实例，
    /// 仅建立一条 PLC TCP 连接，避免多连接被 PLC 拒绝的问题。
    /// </summary>
    private readonly PlcService _plcService = new PlcService(PlcClient.Instance);

    /// <summary>
    /// 顺移服务，负责将"A 笼"中的所有玻璃数据迁移到"B 笼"，
    /// 并通过 PLC 握手验证物理顺移动作已完成。
    /// </summary>
    private readonly ShiftService _shiftService = new ShiftService();

    /// <summary>
    /// 日志仓库，当配置项 EnableActivityLog=1 时，将窗口日志同步写入数据库 Logs 表。
    /// </summary>
    private readonly LogRepository _logRepository = new LogRepository();

    /// <summary>
    /// 标记入笼服务当前是否处于运行状态。
    /// true  → 服务已启动，"启动服务"按钮禁用，"停止服务"按钮可用；
    /// false → 服务已停止，"启动服务"按钮可用，"停止服务"按钮禁用。
    /// </summary>
    private bool _isServiceRunning;

    /// <summary>
    /// 构造函数：初始化窗口组件，并完成以下一次性初始化操作：
    ///   1. 将三个 ObservableCollection 分别绑定到对应的 DataGrid.ItemsSource，
    ///      使后续对集合的增删操作能自动同步到 UI。
    ///   2. 调用 InitializeDatabase() 确保 MySQL 表结构已创建（EnsureCreated）。
    ///   3. 调用 LoadPlanData() 从缓存或数据库加载当前计划数据，填充 dgPlan。
    ///   4. 调用 LoadConfigParams() 将配置文件中的关键参数显示到界面参数栏。
    ///   5. 订阅 InboundService 的四个事件：
    ///      - OnLogActivity → DispatchToLog("TRACE", ...)：将服务日志转发到日志文本框；
    ///      - OnExceptionRecord → AddException(...)：将异常信息插入异常列表头部；
    ///      - OnCompareRecord → AddCompareRecord(...)：将比对结果插入比对列表头部；
    ///      - OnInboundStatusChanged → LoadPlanData()：每次入库事务完成后刷新计划列表。
    ///   6. 订阅 PlcService 的 OnLogActivity 事件，将底层读写日志以"PLC"级别转发到界面。
    ///   7. 调用 UpdateServiceButtons() 根据初始状态设置按钮可用性。
    ///   8. 向 PLC 写入系统启动信号（Addr_SystemStart=1），通知现场设备上位机已就绪。
    /// </summary>
    public InboundWindow()
    {
        // 初始化 XAML 中定义的所有 UI 控件
        InitializeComponent();

        // 初始化语言服务
        LanguageService.Initialize();
        
        // 应用当前语言设置
        ApplyLanguage();
        UpdateLanguageMenuSelection(LanguageService.CurrentLanguage);

        // 将数据集合绑定到各 DataGrid
        // 绑定后，后续只需操作集合（Add/Insert/Clear）即可自动驱动 UI 更新，无需手动刷新
        dgPlan.ItemsSource = _planItems;
        dgExceptions.ItemsSource = _exceptionRecords;
        dgCompare.ItemsSource = _compareRecords;

        // 确保数据库表结构存在；若首次运行，EnsureCreated 会自动建表
        InitializeDatabase();
        // 加载入库计划数据，填充 dgPlan（启动时强制从数据库读取，忽略可能残留的 Redis 旧缓存）
        LoadPlanData(forceRefresh: true);
        // 将配置文件中的运行参数显示到界面参数区
        LoadConfigParams();

        // 将 InboundService 内部产生的文本日志以"TRACE"级别转发到界面日志文本框
        // Lambda 中通过 DispatchToLog 确保切换到 UI 线程后再操作控件
        _inboundService.OnLogActivity += msg => DispatchToLog("TRACE", msg);

        // 将 PlcService 底层 TCP 读写日志以"PLC"级别转发到界面日志文本框
        // 使用具名方法订阅，以便 OnClosed 中能准确 -= 取消订阅，
        // 避免本窗口关闭后仍从单例 PlcClient 接收到老日志（以及内存泄漏）。
        _plcService.OnLogActivity += OnPlcLog;

        // 异常事件：需先切换到 UI 线程（Dispatcher.InvokeAsync）才能操作 ObservableCollection
        _inboundService.OnExceptionRecord += msg => Dispatcher.InvokeAsync(() => AddException(msg));

        // 比对完成事件：同样需要在 UI 线程中插入比对记录
        _inboundService.OnCompareRecord += (ccd, res) => Dispatcher.InvokeAsync(() => AddCompareRecord(ccd, res));

        // 入库状态变更事件：每次入库事务完成（成功/失败/异常）后，自动在 UI 线程刷新计划列表
        _inboundService.OnInboundStatusChanged += () => Dispatcher.InvokeAsync(() => LoadPlanData());

        // 根据初始 _isServiceRunning=false 设置按钮状态（启动可用，停止禁用）
        UpdateServiceButtons();

        // 记录系统启动完成的日志
        AppendLog("INFO", "系统启动完成");

        // 在窗口 Loaded 事件中异步写入 PLC 启动信号
        // 避免在构造函数里同步调用导致 PLC 不可达时窗口出现延迟（界面卡顿）
        Loaded += OnWindowLoaded;
    }

    /// <summary>
    /// 窗口 Loaded 事件处理器：在后台线程向 PLC 写入系统启动信号（Addr_SystemStart=1）。
    /// 放在 Loaded 而非构造函数，是为了避免 PLC 连接超时导致窗口出现延迟（卡顿数秒才显示）。
    /// 写入失败时仅记录日志，不影响程序正常启动流程。
    /// </summary>
    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                // PLC 系统启动信号已移至 BtnStartService_Click，窗口加载时不再自动写入
                Dispatcher.InvokeAsync(() => AppendLog("INFO", "窗口已加载，请点击[启动服务]按钮开始运行"));
            }
            catch (Exception ex)
            {
                Dispatcher.InvokeAsync(() => AppendLog("WARN", $"PLC 启动信号发送失败: {ex.Message}"));
            }
        });
    }

    /// <summary>
    /// 将来自后台线程的日志信息安全地分发到 UI 线程并追加到日志文本框。
    /// 由于 InboundService 和 PlcService 的事件可能在非 UI 线程上触发，
    /// 必须通过 Dispatcher.InvokeAsync 将操作切换到 UI 线程，避免跨线程异常。
    /// </summary>
    /// <param name="level">日志级别字符串，如 "INFO"、"ERROR"、"TRACE"、"PLC"。</param>
    /// <param name="msg">日志内容正文。</param>
    private void DispatchToLog(string level, string msg)
    {
        // InvokeAsync：将 AppendLog 调用异步排队到 UI 线程的消息队列
        // 不会阻塞调用方（后台线程），适合高频日志场景
        Dispatcher.InvokeAsync(() => AppendLog(level, msg));
    }

    /// <summary>
    /// 根据 _isServiceRunning 的当前值同步更新"启动服务"和"停止服务"两个按钮的可用性。
    /// 规则：
    ///   - 服务运行中（_isServiceRunning=true）：启动按钮禁用，防止重复启动；停止按钮可用。
    ///   - 服务已停止（_isServiceRunning=false）：启动按钮可用；停止按钮禁用，防止重复停止。
    /// 此方法仅在 UI 线程中调用，无需 Dispatcher 切换。
    /// </summary>
    private void UpdateServiceButtons()
    {
        btnStartService.IsEnabled = !_isServiceRunning;
        btnStopService.IsEnabled = _isServiceRunning;
    }

    /// <summary>
    /// 初始化数据库：确保所有 EF Core 实体对应的数据库表已存在。
    /// 使用 EnsureCreated() 而非 Migrate()，适用于小型本地数据库或首次部署场景。
    /// 若数据库文件/连接字符串配置有误，则捕获异常并记录错误日志（不抛出，避免程序崩溃）。
    /// </summary>
    private void InitializeDatabase()
    {
        try
        {
            // 使用 using 保证 DbContext 在操作完成后立即释放连接资源
            using var context = new WarehouseDbContext();
            // EnsureCreated：若数据库已存在则不执行任何操作；若不存在则自动建库建表
            context.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            // 数据库初始化失败时记录错误，但不中断程序启动流程
            AppendLog("ERROR", $"数据库初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从 AppConfig（通常对应 appsettings.json 或本地 INI 配置文件）中读取关键运行参数，
    /// 并将其格式化后显示到界面参数栏的对应 TextBlock 控件中：
    ///   - txtDataSource：数据来源类型（0=本地数据库，1=远程 ERP 接口等）；
    ///   - txtGapSize   ：相邻玻璃之间的安全间距（单位：mm）；
    ///   - txtScanRetry ：条码扫描失败时的最大重试次数；
    ///   - txtSortMethod：测量误差允许范围（单位：mm），用于 CCD 比对判定。
    /// 若读取配置失败（键不存在或类型错误），记录 WARN 日志而不抛出异常。
    /// </summary>
    private void LoadConfigParams()
    {
        try
        {
            txtDataSource.Text = $"数据来源: {AppConfig.GetInt("DataSourceType")}";
            txtGapSize.Text = $"安全间距: {AppConfig.GetFloat("GlassSpacing")}";
            txtScanRetry.Text = $"扫码重试: {AppConfig.GetInt("MaxScanRetryTimes")}";
            txtSortMethod.Text = $"测量误差: {AppConfig.GetFloat("MeasureErrorAllowance")}";
        }
        catch (Exception ex)
        {
            // 配置读取失败仅记录警告，界面参数栏可能显示旧值或空值，但不影响核心功能
            AppendLog("WARN", $"加载配置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从缓存（CacheQueryService）或数据库加载当前有效的入库计划数据，
    /// 并刷新 _planItems 集合以驱动 dgPlan 重绘。
    ///
    /// 查询条件：
    ///   - 排除已出库（Outbounded）、异常（Error）状态的物料；破损（Damaged）仍保留在列表中；
    ///   - 按 InboundTime 升序排列（最早待入库的物料排在前面）；
    ///
    /// 缓存策略：
    ///   - 缓存键：plan:materials:all；
    ///   - 缓存有效期：30 秒；超期后自动回源数据库重新查询。
    ///
    /// 此方法在 UI 线程中调用（OnInboundStatusChanged 事件已通过 Dispatcher.InvokeAsync 保证）。
    /// </summary>
    private void LoadPlanData(bool forceRefresh = false)
    {
        // 清空现有数据，防止重复追加
        _planItems.Clear();

        // forceRefresh=true 时先删除 Redis 旧缓存，再走正常读取流程（命中空缓存→查DB→回写缓存）
        // 这样后续所有 LoadPlanData() 调用也能看到最新数据，而不是持续读取陈旧的 Redis 值。
        if (forceRefresh)
            CacheQueryService.ClearCacheByKeys("plan:materials:all");

        var materials = CacheQueryService.GetCachedData<Material>(
            "plan:materials:all",
            () =>
            {
                using var ctx = new WarehouseDbContext();
                // 不使用 Include，避免 Layer→Material WithMany() 关联导致笛卡尔积重复行
                var list = ctx.Materials
                    .Where(m => m.Status != MaterialStatus.Outbounded) // 仅隐藏已出库；破损/异常等均保留显示
                    .AsEnumerable() // <-- 核心：加在这里！意思是先把数据从数据库捞出来
                    .OrderBy(m => m.InboundTime)
                    .ToList();


                // 手动批量加载所需 Order，再逐条挂载，不产生多余 JOIN
                // 用 TryAdd 而非 ToDictionary，防止 Orders 表 OrderID 存在重复行时抛异常
                var orderIds = list
                    .Select(m => m.OrderID)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Distinct()
                    .ToList();
                var orderMap = new Dictionary<string, Order>(StringComparer.Ordinal);
                foreach (var o in ctx.Orders.Where(o => orderIds.Contains(o.OrderID)))
                    orderMap.TryAdd(o.OrderID, o);
                foreach (var m in list)
                    if (m.OrderID != null && orderMap.TryGetValue(m.OrderID, out var ord))
                        m.Order = ord;

                return list;
            },
            TimeSpan.FromSeconds(30)
        );

        // 将查询结果逐条转换为 ViewModel 并加入集合
        // RowNo 为界面显示的序号，从 1 开始递增
        var row = 1;
        foreach (var m in materials)
        {
            _planItems.Add(new PlanItemViewModel
            {
                RowNo = row++,                                    // 界面序号（从1开始）
                OrderNo = m.Order?.OrderNo ?? string.Empty, // 订单编号
                OrderName = m.OrderName ?? string.Empty, // 订单名称
                Length = m.Length,                                 // 玻璃长边尺寸（mm）
                Width = m.Width,                                   // 玻璃短边尺寸（mm）
                ID = m.GlassID,                                    // 玻璃唯一编号
                Name = m.ProductName ?? string.Empty,              // 产品名称
                OriginalProduct = m.ProductName ?? string.Empty,   // 原始产品名称（用于详情展示）
                ClientName = m.Order?.CustomerName ?? string.Empty,// 客户名称
                MaterialID = m.GlassID,                            // 用于操作时定位记录的 ID
                Material = m,                                       // 保留完整实体引用，供详情面板使用
                IsInStock = m.Status == MaterialStatus.InStock,
                IsDamaged = m.Status == MaterialStatus.Damaged || m.IsDamaged == true,
                IsError = m.Status == MaterialStatus.Error,
                StatusText = m.Status switch
                {
                    MaterialStatus.Pending => "待入库",
                    MaterialStatus.InStock => "在库",
                    MaterialStatus.Locked => "锁定",
                    MaterialStatus.Damaged => "破损",
                    MaterialStatus.Error => "异常",
                    _ => m.Status.ToString()
                }
            });
        }

        // 诊断日志：显示各状态数量，帮助核查数据库实际记录分布
        var pending = materials.Count(m => m.Status == MaterialStatus.Pending);
        var inStock = materials.Count(m => m.Status == MaterialStatus.InStock);
        var damaged = materials.Count(m => m.Status == MaterialStatus.Damaged);
        var locked = materials.Count(m => m.Status == MaterialStatus.Locked);
        var error = materials.Count(m => m.Status == MaterialStatus.Error);
        var other = materials.Count - pending - inStock - damaged - locked - error;
        AppendLog("INFO",
            $"计划列表已刷新：共 {materials.Count} 条 " +
            $"（待入库={pending} 在库={inStock} 破损={damaged} 锁定={locked} 异常={error}" +
            (other > 0 ? $" 其他={other}" : "") + ")");
    }

    /// <summary>
    /// 菜单"退出"点击事件处理器。
    /// 执行顺序：
    ///   1. 停止入笼服务（避免后台线程继续运行导致资源泄漏）；
    ///   2. 向 PLC 写入系统停止信号（Addr_SystemStart=0），通知现场设备上位机已离线；
    ///   3. 调用 Application.Current.Shutdown() 关闭整个 WPF 应用程序进程。
    /// 步骤 1、2 包裹在 try-catch 中：即使发生异常（如 PLC 断连）也不阻止程序退出。
    /// </summary>
    private void BtnSwitchToOutbound_Click(object sender, RoutedEventArgs e)
    {
        try { _inboundService.Stop(); } catch { }
        var outboundWindow = new OutboundWindow();
        outboundWindow.Show();
        Close();
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 通知后台三条线程
            _inboundService.Stop();
            // 写入停止信号，通知现场设备上位机已离线
            _plcService.WriteBool("Addr_SystemStart", false);
        }
        catch
        {
            // 忽略退出时的所有异常，确保程序能够正常关闭
        }

        // 关闭整个应用程序（结束主线程消息循环）
        Application.Current.Shutdown();
    }

    private void MenuLanguage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag != null)
        {
            string languageCode = menuItem.Tag.ToString()!;
            
            // 切换语言
            LanguageService.SetLanguage(languageCode);
            
            // 更新菜单选中状态
            UpdateLanguageMenuSelection(languageCode);
            
            // 刷新界面文本
            ApplyLanguage();
        }
    }

    private void UpdateLanguageMenuSelection(string languageCode)
    {
        menuLangChinese.IsChecked = (languageCode == "zh-CN");
        menuLangEnglish.IsChecked = (languageCode == "en-US");
        menuLangRussian.IsChecked = (languageCode == "ru-RU");
    }

    private void ApplyLanguage()
    {
        // 更新菜单文本
        menuSystem.Header = LanguageService.GetMenuSystem();
        menuConfig.Header = LanguageService.GetMenuConfig();
        menuPlan.Header = LanguageService.GetMenuPlan();
        menuReport.Header = LanguageService.GetMenuReport();
        menuExit.Header = LanguageService.GetMenuExit();
        
        // 更新窗口标题
        this.Title = LanguageService.Get("Window.Inbound");
    }

    /// <summary>
    /// 窗口关闭事件重写（OnClosed）。
    /// 当用户直接点击窗口右上角"X"关闭窗口时触发。
    /// 此处与 MenuExit_Click 行为一致：向 PLC 写入系统停止信号，
    /// 确保无论通过哪种方式关闭窗口，都能正确通知现场设备。
    /// 同样包裹 try-catch 以防止 PLC 断连时抛出未处理异常。
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        try
        {
            // 写入停止信号，通知现场设备上位机已离线
            _plcService.WriteBool("Addr_SystemStart", false);
        }
        catch
        {
            // 忽略关闭时的所有异常
        }

        // 取消对单例 PlcClient 的日志订阅，避免本窗口关闭后仍被回调、
        // 造成已销毁控件被访问或内存泄漏。
        try { _plcService.OnLogActivity -= OnPlcLog; } catch { }

        // 调用基类实现，触发 Window.Closed 事件及后续清理
        base.OnClosed(e);
    }

    /// <summary>
    /// PlcClient.OnLogActivity 的订阅处理器。
    /// 定义为具名方法（而非 Lambda）是为了能在 OnClosed 中用 -= 准确取消订阅。
    /// </summary>
    private void OnPlcLog(string msg) => DispatchToLog("PLC", msg);

    /// <summary>
    /// 菜单"全局参数"点击事件处理器。
    /// 当前版本弹出提示框说明参数界面尚未集成。
    /// 同时调用 LoadConfigParams() 刷新界面参数栏，确保显示最新配置值。
    /// </summary>
    private void OpenGlobalParams_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 打开参数配置子窗口，替换此提示
        MessageBox.Show("参数界面待接入。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        // 刷新当前界面显示的配置参数（以防配置文件已被外部修改）
        LoadConfigParams();
    }
    // =========================================================================
    // 新增菜单按钮点击事件处理函数
    // =========================================================================

    private void MenuPlan_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 弹窗提醒

        //System.Windows.MessageBox.Show("点击了【计划管理】，请在这里连接并调用『PlanWindow（计划管理窗口）』的实例化与显示函数！", "功能提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

        // 后续开发参考代码：
        var QueryWindow = new QueryWindow();
        QueryWindow.Show();
    }

    private void MenuOneWay_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 弹窗提醒
        System.Windows.MessageBox.Show("点击了【单向台】，请在这里连接并调用『OneWayWindow（单向台窗口）』的实例化与显示函数！", "功能提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

        // 后续开发参考代码：
        // var oneWayWin = new OneWayWindow();
        // oneWayWin.Show();
    }

    private void MenuGlassTrace_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 弹窗提醒
        System.Windows.MessageBox.Show("点击了【小片跟踪】，请在这里连接并调用『GlassTraceWindow（小片跟踪窗口）』的实例化与显示函数！", "功能提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

        // 后续开发参考代码：
        // var traceWin = new GlassTraceWindow();
        // traceWin.Show();
    }

    private void MenuGlobal_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 弹窗提醒
        System.Windows.MessageBox.Show("点击了【系统配置】，请在这里连接并调用『GlobalConfigWindow（系统配置窗口）』的实例化与显示函数！", "功能提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

        // 后续开发参考代码：
        // var globalWin = new GlobalConfigWindow();
        // globalWin.ShowDialog();
    }

    private void MenuIoPort_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 弹窗提醒
        System.Windows.MessageBox.Show("点击了【IO端口】，请在这里连接并调用『IoPortWindow（IO端口监视窗口）』的实例化与显示函数！", "功能提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

        // 后续开发参考代码：
        // var ioWin = new IoPortWindow();
        // ioWin.Show();
    }

    private void MenuRegister_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 弹窗提醒
        System.Windows.MessageBox.Show("点击了【注册】，请在这里连接并调用『RegisterWindow（软件注册激活窗口）』的实例化与显示函数！", "功能提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

        // 后续开发参考代码：
        // var regWin = new RegisterWindow();
        // regWin.ShowDialog();
    }

    private void MenuSlice_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        // 弹窗逻辑提示
        System.Windows.MessageBox.Show(
            "点击了【理片笼/盘片台】，请在这里连接并调用『SliceWindow（理片/盘片管理窗口）』的实例化与显示逻辑！",
            "功能提示",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information
        );

        // 预留实例化参考：
        // var sliceWin = new SliceWindow();
        // sliceWin.Show();
    }


    private void OpenSystemSettings_Click(object sender, RoutedEventArgs e)
    {
        new SystemSettingsWindow().Show();
    }
    private void OpenCanShuGuanLi_Click(object sender, RoutedEventArgs e)
    {
        new CanShuGuanLi().Show();
    }
    private void OpenQuanjvcanshu_Click(object sender, RoutedEventArgs e)
    {
        new Quanjvcanshu().Show();
    }
    private void OpenControlPlc_Click(object sender, RoutedEventArgs e)
    {
        new ControlPlcUser1().Show();
    }
    private void OpenCageLayerManagement_Click(object sender, RoutedEventArgs e)
    {
        new CageLayerManagementWindow().Show();
    }
    private void OpenLicense_Click(object sender, RoutedEventArgs e)
    {
        new LicenseWindow().Show();
    }
    private void MenuCageDashboard_Click(object sender, RoutedEventArgs e)
    {
        new CageDashboardWindow().Show();
    }
    private void OpenCageDashboardB_Click(object sender, RoutedEventArgs e)
    {
        new CageDashboardWindowB().Show();
    }
    private void OpenQueryWindow_Click(object sender, RoutedEventArgs e)
    {
        new QueryWindow().Show();
    }
    private void OpenLogWindow_Click(object sender, RoutedEventArgs e)
    {
        new LogWindow().Show();
    }
    private void OpenMeasurementPlatform_Click(object sender, RoutedEventArgs e)
    {
        new MeasurementPlatformHmiWindow().Show();
    }
    /// <summary>
    /// 菜单"打印标签"点击事件处理器。
    /// 若 dgPlan 中有选中的物料，则打开携带该物料信息的打印窗口（PrintWindowDraggable）；
    /// 否则打开空白打印窗口（允许用户手动输入或选择打印内容）。
    /// </summary>
    private void MenuPrintLabel_Click(object sender, RoutedEventArgs e)
    {
        // 获取当前选中的计划行（强转为 PlanItemViewModel）
        var selected = dgPlan.SelectedItem as PlanItemViewModel;

        if (selected?.Material != null)
        {
            // 有选中物料且其 Material 引用不为 null：带数据打开打印窗口
            new PrintWindowDraggable(selected.Material).Show();
            return;
        }

        // 无选中物料：打开空白打印窗口，用户可手动填写
        new PrintWindowDraggable().Show();
    }

    /// <summary>
    /// 菜单"笼位看板"点击事件处理器。
    /// 当前版本弹出提示框说明看板界面尚未集成。
    /// </summary>

    /// <summary>
    /// "启动服务"按钮点击事件处理器。
    /// 逻辑流程：
    ///   1. 若服务已在运行（_isServiceRunning=true），直接返回，防止重复启动；
    ///   2. 调用 InboundService.Start() 启动后台三条线程；
    ///   3. 设置 _isServiceRunning=true，更新按钮状态，记录"已启动"日志；
    ///   4. 若启动过程抛出异常，记录错误日志并将 _isServiceRunning 重置为 false。
    /// </summary>
    private void BtnStartService_Click(object sender, RoutedEventArgs e)
    {
        // 幂等检查：重复点击时直接忽略，防止多次调用 Start()
        if (_isServiceRunning)
        {
            return;
        }

        try
        {
            // 启动入笼服务的三条后台线程（安全监控、测量生产、入库消费）
            _inboundService.Start();
            // 向 PLC 写入系统启动信号，通知现场设备上位机已就绪
            _plcService.WriteBool("Addr_SystemStart", true);
            _isServiceRunning = true;
            // 同步更新按钮状态：启动按钮禁用，停止按钮可用
            UpdateServiceButtons();
            AppendLog("INFO", "入笼服务已启动");
        }
        catch (Exception ex)
        {
            // 启动失败时记录错误，并将状态重置为未运行
            AppendLog("ERROR", $"入笼服务启动失败: {ex.Message}");
            _isServiceRunning = false;
            UpdateServiceButtons();
        }
    }

    /// <summary>
    /// "停止服务"按钮点击事件处理器。
    /// 逻辑流程：
    ///   1. 若服务未在运行（_isServiceRunning=false），直接返回，防止重复停止；
    ///   2. 调用 InboundService.Stop() 发送停止信号到后台线程（线程会在当前任务完成后退出）；
    ///   3. 无论 Stop() 是否抛出异常，finally 块都会将 _isServiceRunning 重置为 false
    ///      并更新按钮状态，确保 UI 状态与实际一致。
    /// </summary>
    private void BtnStopService_Click(object sender, RoutedEventArgs e)
    {
        // 幂等检查：服务已停止时直接忽略
        if (!_isServiceRunning)
        {
            return;
        }

        try
        {
            // 向后台三条线程发送停止信号（CancellationToken 取消）
            _inboundService.Stop();
            // 向 PLC 写入系统停止信号，通知现场设备上位机已离线
            _plcService.WriteBool("Addr_SystemStart", false);
            AppendLog("INFO", "入笼服务已停止");
        }
        catch (Exception ex)
        {
            // 停止过程中的异常（通常不会发生）记录错误日志
            AppendLog("ERROR", $"入笼服务停止失败: {ex.Message}");
        }
        finally
        {
            // 无论成功与否，都将状态重置并更新按钮
            // 这样即使 Stop() 异常，界面也不会陷入"停止中"的死锁状态
            _isServiceRunning = false;
            UpdateServiceButtons();
        }
    }

    /// <summary>
    /// "PLC 复位"按钮点击事件处理器。
    /// 将 PLC 中两个关键指令地址清零：
    ///   - Addr_In_EnterCmd     ：入笼指令位（清零后 PLC 停止执行入笼动作）；
    ///   - Addr_In_TargetCagePos：目标笼位地址（清零后 PLC 复位到初始位置）。
    /// </summary>
    private void BtnResetPlc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 复位入笼指令位（Bool 类型）
            _plcService.WriteBool("Addr_In_EnterCmd", false);
            // 复位目标笼位地址（Float 类型）
            _plcService.WriteFloat("Addr_In_TargetCagePos", 0f);
            // 复位比较错误信号
            _plcService.WriteBool("Addr_In_CompareError", false);
            // 复位扫码完成信号
            _plcService.WriteBool("Addr_In_ScanOK", false);
            // 复位到位信号
            _plcService.WriteBool("Addr_In_GlassArrived", false);
            AppendLog("INFO", "PLC 指令位已全部复位");
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"PLC 复位失败: {ex.Message}");
            MessageBox.Show($"PLC 复位失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// "导入Excel"按钮点击事件处理器。
    /// 从 Excel 文件（.xlsx）中批量导入订单和物料数据到数据库：
    ///   1. 弹出文件选择对话框，选择 .xlsx 文件；
    ///   2. 使用 EPPlus 解析第一个工作表，根据表头列名映射字段；
    ///   3. 按 OrderID 分组：同一 OrderID 共享一条 Order 记录；
    ///   4. 在数据库事务中批量写入 Order 和 Material，跳过已存在的 GlassID；
    ///   5. 清除缓存并刷新计划列表。
    /// 
    /// Excel 表头要求（列名不区分大小写，顺序不限）：
    ///   必填列：GlassID, Length, Width
    ///   选填列：OrderID, OrderNo, FlowCardNo, CustomerName, ProductName, Thickness
    /// </summary>
    private async void BtnImportExcel_Click(object sender, RoutedEventArgs e)
    {
        // 弹出文件选择对话框
        var dialog = new OpenFileDialog
        {
            Filter = "Excel 文件 (*.xlsx)|*.xlsx",
            Title = "选择要导入的 Excel 文件"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            ExcelPackage.License.SetNonCommercialPersonal("GlassWarehouseSystem");

            using var package = new ExcelPackage(new FileInfo(dialog.FileName));
            var ws = package.Workbook.Worksheets.FirstOrDefault();
            if (ws == null)
            {
                MessageBox.Show("Excel 文件中没有工作表", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 
            if (ws.Dimension == null)
            {
                MessageBox.Show("工作表中没有数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 解析表头，建立列名→列号的映射
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var col = 1; col <= ws.Dimension.End.Column; col++)
            {
                var header = ws.Cells[1, col].Text?.Trim();
                if (!string.IsNullOrEmpty(header))
                    colMap[header] = col;
            }

            // 校验必填列
            if (!colMap.ContainsKey("GlassID") || !colMap.ContainsKey("Length") || !colMap.ContainsKey("Width"))
            {
                MessageBox.Show("Excel 缺少必填列：GlassID, Length, Width", "格式错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 优先读取 .Text，若为空则 fallback 到 .Value.ToString()（处理公式单元格）
            string CellText(int row, string colName)
            {
                if (!colMap.TryGetValue(colName, out var c)) return string.Empty;
                var cell = ws.Cells[row, c];
                var text = cell.Text?.Trim();
                if (string.IsNullOrEmpty(text))
                    text = cell.Value?.ToString()?.Trim() ?? string.Empty;
                return text;
            }

            decimal CellDecimal(int row, string colName)
            {
                if (!colMap.TryGetValue(colName, out var c)) return 0m;
                var cell = ws.Cells[row, c];
                // 若单元格存储的是数值类型（double），直接转换避免格式化误差
                if (cell.Value is double d) return (decimal)d;
                var text = cell.Text?.Trim() ?? cell.Value?.ToString()?.Trim() ?? string.Empty;
                return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
            }

            // 将耗时的数据库操作放到后台线程，避免冻结 UI
            var (importedCount, skippedCount) = await Task.Run(() =>
            {
                using var context = new WarehouseDbContext();
                using var transaction = context.Database.BeginTransaction();

                // 预加载已存在的 GlassID 和 OrderID，用于跳过重复
                var existingGlassIds = new HashSet<string>(context.Materials.Select(m => m.GlassID));
                var existingOrderIds = new HashSet<string>(context.Orders.Select(o => o.OrderID));
                var batchOrders = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
                var orderMaterialCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                var imported = 0;
                var skipped = 0;

                for (var row = 2; row <= ws.Dimension.End.Row; row++)
                {
                    var rawGlassId = CellText(row, "GlassID");
                    if (string.IsNullOrWhiteSpace(rawGlassId)) continue; // 跳过空行

                    // GlassID 中含 '+' 时拆分为多个 ID，每个 ID 单独导入一条物料
                    var glassIds = rawGlassId
                        .Split('+')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray();
                    if (glassIds.Length == 0) continue;

                    // 含 '+' 时所有拆分片共享同一 GroupID（原始拼接串），出库时必须连组出笼
                    string? groupId = glassIds.Length > 1 ? rawGlassId : null;

                    // 确定 OrderID：Excel 中有则使用，否则以 FlowCardNo 替代，再无则自动生成
                    // 同一行拆分出的所有 GlassID 共享同一个 OrderID
                    var orderId = CellText(row, "OrderID");
                    var flowCardNo = CellText(row, "FlowCardNo");
                    if (string.IsNullOrEmpty(orderId))
                        orderId = !string.IsNullOrEmpty(flowCardNo) ? flowCardNo : $"AUTO_{DateTime.Now:yyyyMMdd}_{row}";

                    // （同一批次内相同 OrderID 只建一条）
                    if (!existingOrderIds.Contains(orderId) && !batchOrders.ContainsKey(orderId))
                    {
                        var order = new Order
                        {
                            OrderID = orderId,
                            OrderNo = CellText(row, "OrderNo"),
                            FlowCardNo = flowCardNo,
                            CustomerName = CellText(row, "CustomerName"),
                            Status = 0,
                            CreateTime = DateTime.Now
                        };
                        context.Orders.Add(order);
                        batchOrders[orderId] = order;
                    }

                    // 为每个拆分出的 GlassID 分别创建 Material（共享本行的尺寸和订单信息）
                    foreach (var glassId in glassIds)
                    {
                        // 跳过已存在的物料（同文件内也去重）
                        if (existingGlassIds.Contains(glassId))
                        {
                            skipped++;
                            continue;
                        }

                        var material = new Material
                        {
                            GlassID = glassId,
                            OrderID = orderId,
                            OrderName = CellText(row, "OrderName"),
                            ProductName = CellText(row, "ProductName"),
                            Length = CellDecimal(row, "Length"),
                            Width = CellDecimal(row, "Width"),
                            Thickness = CellDecimal(row, "Thickness"),
                            Status = MaterialStatus.Pending,
                            ImportTime = DateTime.Now,
                            GroupID = groupId
                        };

                        context.Materials.Add(material);
                        existingGlassIds.Add(glassId); // 防止同文件内重复
                        orderMaterialCount.TryGetValue(orderId, out var cnt);
                        orderMaterialCount[orderId] = cnt + 1;
                        imported++;
                    }
                }

                // 回填每个订单的总片数
                foreach (var (oid, count) in orderMaterialCount)
                {
                    var order = batchOrders.TryGetValue(oid, out var bo) ? bo
                              : context.Orders.FirstOrDefault(o => o.OrderID == oid);
                    if (order != null)
                        order.TotalCount = (order.TotalCount ?? 0) + count;
                }

                context.SaveChanges();
                transaction.Commit();
                return (imported, skipped);
            });

            // 清除缓存，强制刷新（回到 UI 线程）
            _inboundService.ClearPlanCache();
            LoadPlanData(forceRefresh: true);

            AppendLog("INFO", $"Excel 导入完成，新增 {importedCount} 条，跳过已存在 {skippedCount} 条");
            MessageBox.Show($"导入完成\n新增: {importedCount}\n跳过（已存在）: {skippedCount}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"Excel 导入失败: {ex.Message}");
            MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// "导出理片汇总"按钮点击事件处理器。
    /// 导出条件：物料已完成入库（InboundTime 有值）且状态不是"待入库"（Pending）或"已出库"（Outbounded）。
    /// 包含从入库到出库的在库物料，主要用于核对在库数量与位置。
    /// 实际导出逻辑委托给 ExportMaterialsToCsv() 通用方法处理。
    /// </summary>
    private void BtnExportSummary_Click(object sender, RoutedEventArgs e)
    {
        ExportMaterialsToCsv(
            "导出理片汇总",       // 操作名称，用于日志和弹窗提示
            "sheet_summary",       // 导出文件名前缀
            m => m.InboundTime.HasValue &&
                 m.Status != MaterialStatus.Pending &&
                 m.Status != MaterialStatus.Outbounded);  // 过滤条件：已入库且未出库
    }

    /// <summary>
    /// "导出待补片数据"按钮点击事件处理器。
    /// 导出条件：物料状态为"待入库"（Pending）且还未完成入库（InboundTime 为 null）。
    /// 该报表用于识别尚未到达仓库、需要补充的玻璃，方便生产调度跟进。
    /// 实际导出逻辑委托给 ExportMaterialsToCsv() 通用方法处理。
    /// </summary>
    private void BtnExportMissing_Click(object sender, RoutedEventArgs e)
    {
        ExportMaterialsToCsv(
            "导出待补片数据",      // 操作名称
            "pending_replenish",   // 导出文件名前缀
            m => m.Status == MaterialStatus.Pending && !m.InboundTime.HasValue);  // 过滤：待入库且无入库记录
    }

    /// <summary>
    /// dgPlan（计划列表）行选中变更事件处理器。
    /// 当用户点击某行后，将该行对应物料的详细信息填充到右侧的"详细信息"面板：
    ///   - txtDetailTitle  ：显示"详细信息 (GlassID)"；
    ///   - txtDetailID     ：玻璃唯一编号；
    ///   - txtDetailFlow   ：流程卡号；
    ///   - txtDetailClient ：客户名称；
    ///   - txtDetailOrder  ：订单编号；
    ///   - txtDetailSize   ：短边/长边/厚度（保留三位小数）；
    ///   - txtDetailProduct：产品名称。
    /// 若未选中任何行（selected 为 null），直接返回，不更新详情面板。
    /// </summary>
    private void DgPlan_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 获取当前选中行的 ViewModel
        var selected = dgPlan.SelectedItem as PlanItemViewModel;
        if (selected == null)
        {
            // 无选中行时不更新详情面板（保留上次选中的内容或空白）
            return;
        }

        // 更新详情面板各字段
        txtDetailTitle.Text = $"详细信息 ({selected.ID})";
        txtDetailID.Text = $"ID: {selected.ID}";
        txtDetailFlow.Text = $"订单号: {selected.OrderNo}";
        txtDetailClient.Text = $"客户: {selected.ClientName}";
        txtDetailOrder.Text = $"订单名称: {selected.OrderName}";
        // 尺寸信息：宽度=短边，长度=长边，保留三位小数显示
        txtDetailSize.Text = $"短边: {selected.Width:F3} 长边: {selected.Length:F3} 厚度: {selected.Material?.Thickness:F3}";
        txtDetailProduct.Text = $"产品名称: {selected.OriginalProduct}";
    }

    /// <summary>
    /// "标记破损"按钮点击事件处理器（async，不阻塞 UI 线程）。
    /// 支持在 dgPlan 中多选物料后批量标记为破损状态：
    ///   1. 若未选中任何行，弹出提示框引导用户先选择物料；
    ///   2. 遍历所有选中物料，依次调用 InboundService.MarkAsDamagedAsync()
    ///      异步将其状态更新为 Damaged 并记录到数据库；
    ///   3. 操作完成后刷新计划列表（移除已标记破损的行）；
    ///   4. 汇总成功/失败数量，记录日志并弹出结果提示框。
    /// 使用 async/await：MarkAsDamagedAsync 是 I/O 密集型操作（数据库更新），
    /// await 可释放 UI 线程，保证界面在操作期间仍可响应（不冻结）。
    /// </summary>
    private async void BtnMarkDamaged_Click(object sender, RoutedEventArgs e)
    {
        // 获取所有选中行（支持多选，需强转为 List<PlanItemViewModel>）
        var selected = dgPlan.SelectedItems.Cast<PlanItemViewModel>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("请先选择要标记破损的物料", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 二次确认
        var preview = string.Join("、", selected.Take(3).Select(i => i.ID));
        if (selected.Count > 3) preview += $"等共 {selected.Count} 片";
        if (MessageBox.Show(
                $"确认将以下物料标记为 破损 ？\n{preview}\n\n此操作不可撤销！",
                "确认标记破损",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var successCount = 0;
        var failedCount = 0;

        // 逐条处理选中的物料（串行 await，保证顺序和日志可读性）
        foreach (var item in selected)
        {
            // 异步调用：更新数据库中该物料的状态为 Damaged，返回是否成功
            if (await _inboundService.MarkAsDamagedAsync(item.MaterialID))
            {
                successCount++;
            }
            else
            {
                failedCount++;
            }
        }

        // 清除缓存并强制刷新计划列表：破损物料将以红色行继续展示。
        // 传 forceRefresh=true 跳过 Redis 缓存，避免缓存清除失败时误用旧数据。
        _inboundService.ClearPlanCache();
        LoadPlanData(forceRefresh: true);
        AppendLog("INFO", $"标记破损完成，成功 {successCount}，失败 {failedCount}");
        // 弹出操作结果汇总
        MessageBox.Show($"标记破损完成\n成功: {successCount}\n失败: {failedCount}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// "手动报工"按钮点击事件处理器：
    /// 将当前在计划列表中选中的一条或多条玻璃手动确认为"已入笼"（InStock）。
    /// 适用于自动入笼流程异常后，操作员在现场确认玻璃已物理放入笼位的场景。
    /// 流程：
    ///   1. 获取 dgPlan 中所有选中行（支持 Ctrl/Shift 多选）；
    ///   2. 弹出二次确认对话框，显示即将报工的数量；
    ///   3. 逐条调用 InboundService.ManualConfirmInboundAsync() 更新数据库状态；
    ///   4. 汇总成功/失败数量，记录日志并刷新计划列表。
    /// </summary>
    private async void BtnManualReport_Click(object sender, RoutedEventArgs e)
    {
        // 收集所有选中行
        var selectedItems = dgPlan.SelectedItems.OfType<PlanItemViewModel>().ToList();
        if (selectedItems.Count == 0)
        {
            MessageBox.Show("请先在计划列表中选择要报工的玻璃", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 二次确认
        if (MessageBox.Show(
                $"确认将选中的 {selectedItems.Count} 片玻璃手动报工（确认入笼）？",
                "手动报工确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        int successCount = 0, failedCount = 0;

        foreach (var item in selectedItems)
        {
            AppendLog("INFO", $"手动报工: GlassID={item.MaterialID}");
            var ok = await _inboundService.ManualConfirmInboundAsync(item.MaterialID);
            if (ok)
                successCount++;
            else
                failedCount++;
        }

        // 刷新计划列表
        LoadPlanData();

        AppendLog("INFO", $"手动报工完成，成功 {successCount}，失败 {failedCount}");
        MessageBox.Show($"手动报工完成\n成功: {successCount}\n失败: {failedCount}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// "顺移 A→B"按钮点击事件处理器（async，不阻塞 UI 线程）。
    /// 顺移操作：将"A 笼"中所有在库玻璃的逻辑位置迁移到"B 笼"，
    /// 并通过 PLC 握手确认物理顺移动作已完成。
    /// 流程：
    ///   1. 调用 ShiftService.ShiftAToBAsync()执行异步顺移；
    ///   2. 根据返回结果（ShiftResult）记录日志；
    ///   3. 若顺移成功：清除计划缓存（防止旧数据残留），刷新计划列表，弹出成功提示；
    ///   4. 若顺移失败：弹出错误提示框，显示失败原因。
    /// </summary>
    private async void BtnShiftAB_Click(object sender, RoutedEventArgs e)
    {
        // 一般性二次确认
        if (MessageBox.Show(
                "确认执行《顺移 A→B》？\n顺移将把 A 笼所有玻璃的逻辑位置迁移到 B 笼，\n请确认现场设备已就绪。",
                "确认顺移",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            AppendLog("INFO", "顺移已取消");
            return;
        }

        // UI 预检查：在调用 Service 之前查询 B 笼是否有在库玻璃，避免物理冲突
        try
        {
            var (bCount, bOrders) = await Task.Run(() =>
            {
                using var ctx = new WarehouseDbContext();
                var bCage = ctx.Cages.AsNoTracking()
                    .FirstOrDefault(c => c.LocationType == "B笼");
                if (bCage == null) return (0, string.Empty);

                var mats = ctx.Materials.AsNoTracking()
                    .Where(m => m.CurrentCage == bCage.CageCode
                             && m.InboundTime != null
                             && m.Status != MaterialStatus.Outbounded)
                    .ToList();

                var count = mats.Count;
                var orders = string.Join(", ", mats
                    .Select(m => m.OrderID ?? "—")
                    .Distinct());
                return (count, orders);
            });

            if (bCount > 0)
            {
                var msg = $"B 笼内已存有 {bCount} 片玻璃（订单: {bOrders}）。\n\n" +
                          "顺移操作会将 A 笼玻璃移入 B 笼，若 B 笼未清空将发生物理冲突！\n\n" +
                          "请确认 B 笼玻璃已实际出库后再执行。是否继续？";
                if (MessageBox.Show(msg, "B 笼冲突警告",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    AppendLog("INFO", "顺移已取消（B 笼有玻璃）");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog("WARN", $"顺移前检查异常: {ex.Message}，继续执行");
        }

        // 异步执行顺移，等待 PLC 握手和数据库更新完成
        var result = await _shiftService.ShiftAToBAsync();

        // 根据结果记录日志（成功=INFO，失败=ERROR）
        AppendLog(result.Success ? "INFO" : "ERROR", $"顺移A->B: {result.Message}");

        if (result.Success)
        {
            // 顺移成功：记录详细信息（A 笼编号、B 笼编号、移动数量）
            AppendLog("INFO", $"顺移完成，A={result.ACageCode} B={result.BCageCode} 移动数量={result.MovedCount}");
            // 清除计划缓存，强制下次查询从数据库重新加载（因为笼位已变更）
            _inboundService.ClearPlanCache();
            // 刷新计划列表，反映最新的笼位分配
            LoadPlanData();
            // 弹出成功结果汇总
            MessageBox.Show($"顺移完成\nA={result.ACageCode}\nB={result.BCageCode}\n移动数量={result.MovedCount}",
                "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 顺移失败：显示错误原因，供操作员处理
        MessageBox.Show($"顺移失败: {result.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// "手动入库"按钮点击事件处理器（async，不阻塞 UI 线程）。
    /// 在自动入库流程异常时，允许操作员手动触发指定物料的入库确认：
    ///   1. 若 dgPlan 未选中任何行，直接返回；
    ///   2. 调用 InboundService.ManualConfirmInboundAsync() 异步执行入库确认
    ///      （更新数据库状态、触发 PLC 入笼指令）；
    ///   3. 根据返回值记录成功或失败日志；
    ///   4. 刷新计划列表，反映最新入库状态。
    /// </summary>
    private async void BtnManualInstock_Click(object sender, RoutedEventArgs e)
    {
        if (dgPlan.SelectedItem is not PlanItemViewModel selected)
            return;

        // 二次确认
        if (MessageBox.Show(
                $"确认将玻璃《{selected.ID}》手动入库？\n请确认玻璃已物理放入笼位。",
                "确认手动入库",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var ok = await _inboundService.ManualConfirmInboundAsync(selected.MaterialID);

        // 记录操作结果日志
        AppendLog(ok ? "INFO" : "ERROR", ok ? "手动入库成功" : "手动入库失败");

        // 刷新计划列表（已入库的物料将根据状态过滤从列表中移除）
        LoadPlanData();
    }

    /// <summary>
    /// "手动完成"按钮点击事件处理器（功能待完善）。
    /// 当前版本仅记录日志，实际"完成"业务逻辑（如标记订单为全部入库完成）待后续集成。
    /// </summary>
    private void BtnManualFinish_Click(object sender, RoutedEventArgs e)
    {
        AppendLog("INFO", "手动完成已记录");
    }

    /// <summary>
    /// 向日志文本框（txtLog）追加一条带时间戳和级别的日志记录，并自动滚动到最新内容。
    /// 格式：[HH:mm:ss] [LEVEL] 消息内容
    /// 当配置项 EnableActivityLog=1 时，同时将该条日志异步写入数据库 Logs 表。
    /// 注意：此方法必须在 UI 线程中调用；后台线程应通过 DispatchToLog() 中转。
    /// </summary>
    /// <param name="level">日志级别，如 "INFO"、"WARN"、"ERROR"、"TRACE"、"PLC"。</param>
    /// <param name="message">日志正文内容。</param>
    private void AppendLog(string level, string message)
    {
        // AppendText 比 Text += 效率更高，避免大字符串拼接
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
        // 滚动到末尾，确保最新日志可见
        txtLog.ScrollToEnd();

        // 读取 EnableActivityLog 配置（默认 "0"），为 "1" 时将窗口日志写入数据库 Logs 表
        // 使用 GetStringOrDefault 防止键不存在时抛出异常（如首次运行未执行 SQL 脚本）
        if (AppConfig.IsInitialized &&
            AppConfig.GetStringOrDefault("EnableActivityLog", "0") == "1")
        {
            var content = $"[{level}] {message}";
            // 异步写入，避免频繁 DB 操作阻塞 UI 线程
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try { _logRepository.Insert(content, level); }
                catch { /* 日志落库失败不影响界面正常运行 */ }
            });
        }
    }

    /// <summary>
    /// 向异常记录列表（dgExceptions）的头部插入一条新的异常记录。
    /// 采用 Insert(0, ...) 而非 Add() 是为了让最新异常显示在列表顶端，
    /// 便于操作员快速定位当前异常。
    /// 此方法必须在 UI 线程中调用（调用方已通过 Dispatcher.InvokeAsync 保证）。
    /// </summary>
    /// <param name="message">异常描述文本，由 InboundService.OnExceptionRecord 事件携带。</param>
    private void AddException(string message)
    {
        // 插入到集合头部，绑定的 DataGrid 会自动将新行显示在顶端
        _exceptionRecords.Insert(0, new ExceptionRecordViewModel
        {
            Time = DateTime.Now.ToString("HH:mm:ss"),  // 异常发生时间（精确到秒）
            Message = message                            // 异常描述内容
        });
    }

    /// <summary>
    /// 向比对记录列表（dgCompare）的头部插入一条新的 CCD 比对记录。
    /// 采用 Insert(0, ...) 使最新比对结果显示在列表顶端（与异常列表策略一致）。
    /// 此方法必须在 UI 线程中调用（调用方已通过 Dispatcher.InvokeAsync 保证）。
    /// </summary>
    /// <param name="ccdData">CCD 原始测量数据字符串，格式通常为"长x宽"或 JSON。</param>
    /// <param name="result">比对结果描述，如"匹配成功: GlassID=xxx"或"未找到匹配记录"。</param>
    private void AddCompareRecord(string ccdData, string result)
    {
        _compareRecords.Insert(0, new CompareRecordViewModel
        {
            Time = DateTime.Now.ToString("HH:mm:ss"),  // 比对完成时间（精确到秒）
            CCDData = ccdData,                          // CCD 测量数据
            Result = result                              // 比对结果描述
        });
    }

    /// <summary>
    /// "流程卡扫码"按钮点击事件处理器：
    /// 弹出扫码小窗口，操作员使用扫码枪或手动输入条码后，
    /// 点击"确认"将条码注入到 InboundService 的处理队列；
    /// 点击"清除"则取消本次扫码操作。
    /// </summary>
    private void BtnScanFlowCard_Click(object sender, RoutedEventArgs e)
    {
        if (BarcodePopupWindow.ShowBarcode(this, out string result))
        {
            _inboundService.InjectMockBarcode(result);
            AppendLog("INFO", $"手工扫码注入: {result}");
        }
        else
        {
            AppendLog("INFO", "手工扫码注入已取消");
        }
    }

    /// <summary>
    /// 通用 CSV 导出方法，供不同导出按钮复用。
    /// 执行流程：
    ///   1. 使用 AsNoTracking() 高效查询数据库，按 predicate 过滤，按 OrderID + GlassID 排序；
    ///   2. 若无可导出数据，弹出提示并返回；
    ///   3. 弹出"另存为"文件对话框，让用户选择保存路径和文件名（默认含时间戳）；
    ///   4. 构建 CSV 内容（表头 + 数据行），每个字段经 EscapeCsv() 处理防止注入；
    ///   5. 以 UTF-8 BOM 编码写入文件（Excel 打开中文不乱码）；
    ///   6. 弹出成功提示，显示导出数量和文件路径；
    ///   7. 若任何步骤出现异常，记录错误日志并弹出错误提示。
    /// </summary>
    /// <param name="actionName">操作名称，用于日志和弹窗标题（如"导出理片汇总"）。</param>
    /// <param name="filePrefix">导出文件名前缀（如"sheet_summary"），最终文件名为 {prefix}_{yyyyMMdd_HHmmss}.csv。</param>
    /// <param name="predicate">EF Core 可翻译的过滤表达式，用于确定导出哪些物料记录。</param>
    private void ExportMaterialsToCsv(string actionName, string filePrefix, Expression<Func<Material, bool>> predicate)
    {
        try
        {
            using var context = new WarehouseDbContext();
            // AsNoTracking：只读查询，不跟踪实体变化，性能更好（无需维护变更跟踪缓存）
            var materials = context.Materials
                .AsNoTracking()
                .Include(m => m.Order)      // 联查 Order 以获取 OrderNo、FlowCardNo、CustomerName
                .Where(predicate)           // 应用调用方传入的过滤条件（EF Core 会转换为 SQL WHERE 子句）
                .OrderBy(m => m.OrderID)    // 主排序：按订单
                .ThenBy(m => m.GlassID)     // 次排序：同订单内按玻璃 ID
                .ToList();

            if (materials.Count == 0)
            {
                // 无数据时提示用户，不弹出文件保存对话框
                MessageBox.Show("没有可导出的数据", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                AppendLog("INFO", $"{actionName}：无可导出数据");
                return;
            }

            // 弹出"另存为"对话框，限制文件类型为 CSV，并生成带时间戳的默认文件名
            var dialog = new SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = $"{filePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",  // 默认文件名含时间戳
                AddExtension = true,
                DefaultExt = ".csv"
            };

            // 用户点击"取消"时 ShowDialog() 返回 null 或 false
            if (dialog.ShowDialog() != true)
            {
                AppendLog("INFO", $"{actionName}已取消");
                return;
            }

            // 构建 CSV 内容：第一行为表头，后续各行为数据
            var lines = new List<string>
            {
                // 表头：与下方数据行的字段顺序严格对应
                "GlassID,OrderID,OrderNo,FlowCardNo,CustomerName,ProductName,Length,Width,Thickness,Status,IsDamaged,CurrentCage,CurrentLayer,SlotNo,InboundTime,OutboundTime"
            };

            foreach (var m in materials)
            {
                // 使用 EscapeCsv 对每个字段进行转义，防止字段值中含有逗号、引号或换行符破坏 CSV 格式
                lines.Add(string.Join(",",
                    EscapeCsv(m.GlassID),
                    EscapeCsv(m.OrderID),
                    EscapeCsv(m.Order?.OrderNo),
                    EscapeCsv(m.Order?.FlowCardNo),
                    EscapeCsv(m.Order?.CustomerName),
                    EscapeCsv(m.ProductName),
                    EscapeCsv(m.Length.ToString("F3", CultureInfo.InvariantCulture)),     // 保留三位小数，使用不变文化避免小数点为逗号
                    EscapeCsv(m.Width.ToString("F3", CultureInfo.InvariantCulture)),
                    EscapeCsv(m.Thickness.ToString("F3", CultureInfo.InvariantCulture)),
                    EscapeCsv(m.Status.ToString()),
                    EscapeCsv((m.IsDamaged ?? false) ? "1" : "0"),                        // 布尔值转为 0/1，便于 Excel 处理
                    EscapeCsv(m.CurrentCage),
                    EscapeCsv(m.CurrentLayer?.ToString(CultureInfo.InvariantCulture)),
                    EscapeCsv(m.SlotNo?.ToString(CultureInfo.InvariantCulture)),
                    EscapeCsv(m.InboundTime?.ToString("yyyy-MM-dd HH:mm:ss")),
                    EscapeCsv(m.OutboundTime?.ToString("yyyy-MM-dd HH:mm:ss"))
                ));
            }

            // 将所有行用系统换行符连接，并以 UTF-8 BOM 编码写入文件
            // UTF8Encoding(true) 的 true 表示添加 BOM，确保 Excel 打开中文内容时不乱码
            var csvContent = string.Join(Environment.NewLine, lines);
            File.WriteAllText(dialog.FileName, csvContent, new UTF8Encoding(true));

            AppendLog("INFO", $"{actionName}成功，已导出 {materials.Count} 条，文件: {dialog.FileName}");
            MessageBox.Show($"导出成功\n数据条数: {materials.Count}\n文件: {dialog.FileName}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            // 捕获文件 I/O 异常、数据库异常等，记录并通知用户
            AppendLog("ERROR", $"{actionName}失败: {ex.Message}");
            MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// CSV 字段转义辅助方法（静态，无副作用）。
    /// 遵循 RFC 4180 标准对 CSV 字段值进行安全转义：
    ///   - 若值为 null 或空字符串，返回空字符串（CSV 空字段）；
    ///   - 若值中包含逗号（,）、双引号（"）、换行符（\n）或回车符（\r）中的任意一个，
    ///     则将整个字段用双引号包裹，并将内部的双引号替换为两个连续双引号（""）；
    ///   - 否则直接返回原始值（无需转义）。
    /// 示例：
    ///   EscapeCsv("abc")         → "abc"
    ///   EscapeCsv("a,b")         → "\"a,b\""
    ///   EscapeCsv("say \"hi\"")  → "\"say \"\"hi\"\"\""
    /// </summary>
    /// <param name="value">需要转义的字段原始值（可为 null）。</param>
    /// <returns>转义后的 CSV 安全字段字符串。</returns>
    private static string EscapeCsv(string? value)
    {
        // null 或空值直接返回空字符串，对应 CSV 的空字段
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // 检测是否含有需要转义的特殊字符
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            // 将字段值用双引号包裹，并将内部双引号替换为 "" 以满足 RFC 4180 规范
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        // 无特殊字符，直接返回原始值
        return value;
    }
}

/// <summary>
/// 入库计划列表（dgPlan）的行数据视图模型。
/// 每个实例对应数据库中一条 Material 记录，
/// 将原始实体数据转换为界面友好的展示格式。
/// </summary>
public class PlanItemViewModel
{
    /// <summary>界面显示序号，从 1 开始，用于给操作员快速定位行位置。</summary>
    public int RowNo { get; set; }

    /// <summary>订单编号，来源于关联的 Order.OrderNo。</summary>
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>订单名称，来源于 Material.OrderName。</summary>
    public string OrderName { get; set; } = string.Empty;

    /// <summary>玻璃长边尺寸，单位：mm，保留三位小数显示。</summary>
    public decimal Length { get; set; }

    /// <summary>玻璃短边尺寸，单位：mm，保留三位小数显示。</summary>
    public decimal Width { get; set; }

    /// <summary>玻璃唯一编号（GlassID），用于在列表中快速识别。</summary>
    public string ID { get; set; } = string.Empty;

    /// <summary>产品名称，来源于 Material.ProductName，用于 dgPlan 列显示。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>原始产品名称，与 Name 保持一致，在详情面板中单独展示。</summary>
    public string OriginalProduct { get; set; } = string.Empty;

    /// <summary>客户名称，来源于关联订单的 Order.CustomerName。</summary>
    public string ClientName { get; set; } = string.Empty;

    /// <summary>物料唯一标识，与 ID 相同，用于操作（标记破损、手动入库）时定位数据库记录。</summary>
    public string MaterialID { get; set; } = string.Empty;

    /// <summary>
    /// 完整的 Material 实体引用，供详情面板展示厚度、订单号等原始数据字段。
    /// 不参与 DataGrid 列绑定，仅在选中行后通过代码访问。
    /// </summary>
    public Material? Material { get; set; }

    /// <summary>是否已入库（InStock），用于 DataGrid 行底色 DataTrigger 绑定。</summary>
    public bool IsInStock { get; set; }

    /// <summary>是否已标记破损，用于 DataGrid 行底色 DataTrigger（浅红背景）。</summary>
    public bool IsDamaged { get; set; }

    /// <summary>是否为系统异常状态，用于 DataGrid 行底色 DataTrigger（橙色背景）。</summary>
    public bool IsError { get; set; }

    /// <summary>状态文本，用于《状态》列显示。</summary>
    public string StatusText { get; set; } = string.Empty;
}

/// <summary>
/// CCD 比对记录（dgCompare）的行数据视图模型。
/// 每个实例记录一次 CCD 测量与数据库物料的比对过程结果。
/// </summary>
public class CompareRecordViewModel
{
    /// <summary>比对完成时间，格式 "HH:mm:ss"，精确到秒。</summary>
    public string Time { get; set; } = string.Empty;

    /// <summary>CCD 相机采集到的原始测量数据，通常包含长边和短边尺寸信息。</summary>
    public string CCDData { get; set; } = string.Empty;

    /// <summary>比对结果描述，如"匹配成功: GlassID=xxx"或"未找到匹配记录（误差超限）"。</summary>
    public string Result { get; set; } = string.Empty;
}

/// <summary>
/// 异常记录（dgExceptions）的行数据视图模型。
/// 每个实例记录一条由 InboundService 在入库过程中检测到的异常信息。
/// </summary>
public class ExceptionRecordViewModel
{
    /// <summary>异常发生时间，格式 "HH:mm:ss"，精确到秒。</summary>
    public string Time { get; set; } = string.Empty;

    /// <summary>异常描述文本，由 InboundService.OnExceptionRecord 事件携带，说明异常类型和上下文。</summary>
    public string Message { get; set; } = string.Empty;
}
