using System;
using System.Threading;
using GlassWarehouseSystem.Services;

namespace TestScanner
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("===================================");
            Console.WriteLine("初始化相机扫描服务测试...");
            try
            {
                using var scanner = new CameraScannerService();
                
                scanner.OnBarcodeScanned += (sender, barcode) =>
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n[识别成果] {DateTime.Now:HH:mm:ss.fff} >>> 扫到内容: {barcode}");
                    Console.ResetColor();
                };

                Console.WriteLine("正在连接相机...");
                scanner.ConnectCamera();
                Console.WriteLine("相机连接成功！");

                Console.WriteLine("开始扫描...");
                scanner.StartScanning();

                Console.WriteLine("相机制图和扫码线程运行中...");
                Console.WriteLine("===================================");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("提示：请将带有条码/二维码的物品放在镜头前！");
                Console.WriteLine("如果您想要相机重新自动对焦，请在键盘上按 'F' 键。");
                Console.WriteLine("按 'ESC' 退出测试程序。");
                Console.ResetColor();
                
                // Keep the program running until a key is pressed.
                while (true)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Escape)
                        {
                            break;
                        }
                        else if (key.Key == ConsoleKey.F)
                        {
                            Console.WriteLine("向相机发送重新对焦指令...");
                            scanner.TriggerAutofocus();
                        }
                    }
                    Thread.Sleep(50);
                }
                
                Console.WriteLine("\n正在断开相机...");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n发生错误：{ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
                Console.ReadKey();
            }
            Console.WriteLine("测试结束。");
        }
    }
}
