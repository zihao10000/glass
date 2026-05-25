using System.Windows;

namespace PlcModbusTool
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show(
                    "错误：\n\n" + ex.Exception.Message + "\n\n" + ex.Exception.StackTrace,
                    "程序异常",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                ex.Handled = true;
            };
        }
    }
}
