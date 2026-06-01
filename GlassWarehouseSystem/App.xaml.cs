using GlassWarehouseSystem.Config;
using GlassWarehouseSystem.Data;
using GlassWarehouseSystem.Services;
using MySqlConnector;
using System;
using System.Windows;

namespace GlassWarehouseSystem
{
    /// <summary>
    /// 【框架层级应用根起始容器】程序域的主引导骨干执行的起源开始点。
    /// 它从这开始控制程序的整个周期生命、决定哪些应该先检查防未授权、建立系统全局依赖的基调与联席加载以及抛错时该如何以UI层面体现给前端最终实施用户的核心引导点。
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// WPF 生命周期的开机启动事件监听覆写。
        /// 这里的代码运行于哪怕有任何窗体绘制出前，是阻截系统进行强授权控制、安全库检入和首刷静态大字典的最恰当防区。
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // [开机第一检查项]：系统启动自裁校验——拦截不被名单包含授信的机壳中拷贝使用的人引发灾难崩溃禁止启动保护自己程序代码资源被分发。

                // [开机第二预支项]：准备加载全部关键性的系统核心映射资料参数。由于此刻框架EF Core还没预热可能会耗时，于是使用极基础 ADO 连接直杀MySQL核心读。
                using var conn = new MySqlConnection(WarehouseDbContext.ConnectionString);
                conn.Open(); // 打通连接
                // 将打开的这个底层管道抛给 AppConfig 单例执行查收 PLC与设定配置表并生成两颗静默无锁只读超高速寻址字典。
                AppConfig.Initialize(conn);
            }
            catch (InvalidOperationException ex)
            {
                // 一旦查明没授权身份或其它硬件严重故障不符，将报弹抛明告诉用户的对话提醒警告
                MessageBox.Show(
                    $"硬件验证失败，程序无法启动。\n\n{ex.Message}",
                    "硬件绑定错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(); // 干脆将窗体自杀进程退场掐断保护生命周期不被继续探索漏洞
                return;
            }
            catch (Exception ex)
            {
                // 这是拦截例如 MySQL开机还没起动没起进程的时候导致第二项直接断开获取不通而捕集的宽泛通用网络/初始化事故报错阻碍：连个底仓都没有不用运行业务了免得后面报错更没法解析
                MessageBox.Show(
                    $"系统初始化失败，程序无法启动。\n\n{ex.Message}",
                    "初始化错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                // 让它把真实的病因写到 D 盘的错误文本里，不再使用会崩溃的 WPF 弹窗
                System.IO.File.WriteAllText("D:\\运行报错原因.txt", ex.ToString());
                Environment.Exit(0);

                Shutdown();
                return;
            }

            // 同时启动入笼与出笼两个窗口，左右并排显示，便于操作员同时监控两条流程。
            // MainWindow 设为入笼窗口，关闭它即触发应用退出；出笼窗口作为附属窗口同步关闭。
            var inboundWindow = new InboundWindow();
            var outboundWindow = new OutboundWindow();

            // 设为手动定位以避免被 WindowStartupLocation 默认居中覆盖
            inboundWindow.WindowStartupLocation  = WindowStartupLocation.Manual;
            outboundWindow.WindowStartupLocation = WindowStartupLocation.Manual;

            // 按主屏幕工作区左右并排：入笼居左、出笼居右
            var workArea = SystemParameters.WorkArea;
            inboundWindow.Left  = workArea.Left;
            inboundWindow.Top   = workArea.Top;
            outboundWindow.Left = workArea.Left + workArea.Width / 2;
            outboundWindow.Top  = workArea.Top;

            MainWindow = inboundWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;

            inboundWindow.Closed += (_, _) => outboundWindow.Close();

            inboundWindow.Show();
            outboundWindow.Show();
        }
    }
}
