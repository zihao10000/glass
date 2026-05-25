using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using GlassWarehouseSystem.Services;

namespace TestInboundFlow
{
    class Program
    {
        static void Main()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("        GlassWarehouseSystem 入库流程步进测试       ");
            Console.WriteLine("==================================================");

            try
            {
                Console.WriteLine("正在连接数据库并加载系统核心配置...");
                using var conn = new MySqlConnector.MySqlConnection(GlassWarehouseSystem.Data.WarehouseDbContext.ConnectionString);
                conn.Open();
                GlassWarehouseSystem.Config.AppConfig.Initialize(conn);

                var plcClient = new GlassWarehouseSystem.Services.PlcClient();
                var plcService = new GlassWarehouseSystem.Services.PlcService(plcClient);
                var cageFinder = new GlassWarehouseSystem.Services.CageFinder(new GlassWarehouseSystem.Repositories.CageRepository());
                var logRepo = new GlassWarehouseSystem.Repositories.LogRepository();
                var service = new InboundService(plcService, cageFinder, logRepo);

                // --- 实时侦听核心 PLC 地址跳变 ---
                var _addressesToWatch = new[] {
                    "Addr_GlobalEStop",
                    "Addr_In_GlassArrived",
                    "Addr_In_TargetCagePos",
                    "Addr_In_PosReached",
                    "Addr_In_EnterCmd",
                    "Addr_In_EnterDone"
                };
                var _lastStates = new Dictionary<string, short>();
                foreach (var addr in _addressesToWatch) _lastStates[addr] = -1;

                var _monitorCts = new CancellationTokenSource();
                Task.Run(() =>
                {
                    while (!_monitorCts.Token.IsCancellationRequested)
                    {
                        foreach (var addr in _addressesToWatch)
                        {
                            try {
                                var current = plcService.ReadShort(addr);
                                if (_lastStates[addr] != current && _lastStates[addr] != -1) {
                                    Console.ForegroundColor = ConsoleColor.Magenta;
                                    Console.WriteLine($"\n[PLC 状态发生改变] >> {addr} 的值从 {_lastStates[addr]} 变成了 {current}");
                                    Console.ResetColor();
                                }
                                _lastStates[addr] = current;
                            } catch { }
                        }
                        Thread.Sleep(300);
                    }
                });
                // ------------------------------------

                // 订阅底层业务的所有日志并染色输出
                service.OnLogActivity = msg =>
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[系统日志] {DateTime.Now:HH:mm:ss.fff} - {msg}");
                    Console.ResetColor();
                };

                // 设置单步调试的拦截逻辑：通过终端显示即将发生的动作，但自动放行交由纯 PLC 控制
                service.StepInterceptor = prompt =>
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[系统决策即将下发] {prompt}");
                    Console.ResetColor();
                    return true;
                };

                Console.WriteLine("正在启动 InboundService 及其依赖的所有后台线程...");
                service.Start();

                Console.WriteLine("\n程序已在后台畅通运行！[无按键阻塞模式]");
                Console.WriteLine("重要提示：只需用您的 file_1 脚本直接向 PLC 写入 Addr_In_GlassArrived = 1 ！");
                Console.WriteLine("系统将自动挂载数据库首档物料并驱动后续判定，您只需顺着紫色的 [PLC状态] 提示，不停在 file_1 中配合上位机喂状态即可！");
                Console.WriteLine("按 [ESC] 彻底退出该程序。\n");

                while (true)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.M)
                        {
                            Console.WriteLine("\n[调试辅助] 请输入要塞入系统队列的模拟条码（例如 GL1001），打完回车提交：");
                            Console.ForegroundColor = ConsoleColor.Green;
                            var mockCode = Console.ReadLine();
                            Console.ResetColor();
                            if (!string.IsNullOrWhiteSpace(mockCode))
                            {
                                service.InjectMockBarcode(mockCode.Trim());
                            }
                        }
                        else if (key.Key == ConsoleKey.Escape) break;
                    }
                    Thread.Sleep(50);
                }

                Console.WriteLine("正在安全关闭服务中...");
                _monitorCts.Cancel();
                service.Stop();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n启动失败，遇到致命错误：\n{ex}");
                Console.ResetColor();
                Console.ReadKey();
            }
            
            Console.WriteLine("测试结束。");
        }
    }
}
