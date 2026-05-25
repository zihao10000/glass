using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GlassWarehouseSystem.Services;

namespace GlassWarehouseSystem
{
    /// <summary>
    /// BarcodePopupWindow.xaml
    /// </summary>
    public partial class BarcodePopupWindow : Window
    {
        public string Barcode { get; private set; } = string.Empty;

        /// <summary>相机扫码超时时间（毫秒）</summary>
        private const int CameraScanTimeoutMs = 10000;
        
        public BarcodePopupWindow()
        {
            InitializeComponent();
            this.Loaded += BarcodePopupWindow_Loaded;
        }

        private void BarcodePopupWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 窗体加载时，自动将焦点置于 TextBox，等待扫码枪输入
            txtBarcode.Focus();
        }

        private void txtBarcode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // 扫码枪最后会触发回车：将输入内容转移到结果框，清空输入框，焦点移到确认按钮
                var text = txtBarcode.Text.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    txtResult.Text = text;
                    txtBarcode.Text = string.Empty;
                }
                btnConfirm.Focus();
                e.Handled = true;
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            // 清除结果框和输入框，重新等待扫码
            Barcode = string.Empty;
            txtResult.Text = string.Empty;
            txtBarcode.Text = string.Empty;
            txtBarcode.Focus();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            // 确认结果框中的内容；若结果框为空则尝试使用输入框内容
            Barcode = txtResult.Text.Trim();
            if (string.IsNullOrEmpty(Barcode))
                Barcode = txtBarcode.Text.Trim();

            if (string.IsNullOrEmpty(Barcode))
            {
                MessageBox.Show("请先扫描或输入条码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtBarcode.Focus();
                return;
            }
            this.DialogResult = true;
            this.Close();
        }

        /// <summary>
        /// "相机扫码"按钮点击事件处理器：
        /// 连接海康相机 → 启动扫描 → 等待识别到条码（或超时） → 将结果填入文本框 → 断开相机。
        /// 扫码在后台线程执行，UI 保持响应。
        /// </summary>
        private async void BtnCameraScan_Click(object sender, RoutedEventArgs e)
        {
            btnCameraScan.IsEnabled = false;
            btnCameraScan.Content = "扫码中...";
            txtBarcode.Text = string.Empty;

            string? scannedCode = null;

            try
            {
                scannedCode = await Task.Run(() =>
                {
                    using var scanner = new CameraScannerService();
                    string? result = null;
                    var waitHandle = new ManualResetEventSlim(false);

                    scanner.OnBarcodeScanned += (s, barcode) =>
                    {
                        if (!string.IsNullOrEmpty(barcode))
                        {
                            result = barcode;
                            waitHandle.Set();
                        }
                    };

                    scanner.ConnectCamera();
                    scanner.StartScanning();

                    // 等待扫码结果或超时
                    waitHandle.Wait(CameraScanTimeoutMs);

                    scanner.StopScanning();
                    scanner.Disconnect();

                    return result;
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"相机扫码失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnCameraScan.IsEnabled = true;
                btnCameraScan.Content = "相机扫码";
            }

            if (!string.IsNullOrEmpty(scannedCode))
            {
                txtResult.Text = scannedCode;
                txtBarcode.Text = string.Empty;
                btnConfirm.Focus();
            }
            else
            {
                MessageBox.Show("未识别到条码，请重试或手动输入", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtBarcode.Focus();
            }
        }

        /// <summary>
        /// 弹出扫码小窗口，获取用户扫码内容
        /// </summary>
        /// <param name="owner">父窗口</param>
        /// <param name="result">返回的条码</param>
        /// <returns>如果确认则返回true，否则false</returns>
        public static bool ShowBarcode(Window owner, out string result)
        {
            result = string.Empty;
            
            var popup = new BarcodePopupWindow();
            popup.Owner = owner;
            
            if (popup.ShowDialog() == true)
            {
                result = popup.Barcode;
                return true;
            }
            
            return false;
        }
    }
}
