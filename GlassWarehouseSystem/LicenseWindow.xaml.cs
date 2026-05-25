using System;
using System.Windows;
using GlassWarehouseSystem.Services.License;
using System.Windows.Input;

namespace GlassWarehouseSystem
{
    public partial class LicenseWindow : Window
    {
        public LicenseWindow()
        {
            InitializeComponent();
            // 在授权窗口中显示当前本机硬件指纹（机器码/获取机器码），方便直接复制
            try
            {
                RefreshMachineCode();
            }
            catch (Exception ex)
            {
                txtHwHash.Text = $"获取硬件指纹失败: {ex.Message}";
            }
        }

        private void RefreshMachineCode()
        {
            string hwHash = HardwareFingerprint.GetHardwareHash();
            txtHwHash.Text = hwHash;
        }

        private void BtnGetMachineCode_Click(object sender, RoutedEventArgs e)
        {
            // 重新刷新一遍，避免硬件指纹变化导致的显示不一致
            try
            {
                RefreshMachineCode();
            }
            catch (Exception ex)
            {
                txtHwHash.Text = $"获取硬件指纹失败: {ex.Message}";
            }
        }

        private void BtnImportFile_Click(object sender, RoutedEventArgs e)
        {
            // 导入注册文件暂不实现，按钮只用于与现有排版保持一致
            MessageBox.Show("导入注册文件功能暂未实现。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            string licenseText = txtSerial.Text.Trim();
            if (string.IsNullOrEmpty(licenseText))
            {
                MessageBox.Show("请输入授权码。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (LicenseVerifier.TryVerify(licenseText, out var payload, out var error))
            {
                // 授权通过，如有需要可以使用 payload.LicenseId 做记录
                LicenseStorageService.SaveLicense(licenseText);
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show(error, "授权失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void txtSerial_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 允许 TextBox 在显示层面“视觉换行”，但禁止通过 Enter 真正写入换行符。
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                BtnOk_Click(sender, new RoutedEventArgs());
            }
        }
    }
}

