using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GlassWarehouseSystem.Services.License
{
    /// <summary>
    /// 本地离线保存授权码（DPAPI 加密），用于在过期前免重复输入。
    /// </summary>
    public static class LicenseStorageService
    {
        private static string LicenseFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GlassWarehouseSystem", "license.dat");

        public static void SaveLicense(string licenseText)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LicenseFilePath)!);

            var plainBytes = Encoding.UTF8.GetBytes(licenseText);
            var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(LicenseFilePath, protectedBytes);
        }

        public static string? TryLoadLicense()
        {
            if (!File.Exists(LicenseFilePath))
                return null;

            try
            {
                var protectedBytes = File.ReadAllBytes(LicenseFilePath);
                var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                var text = Encoding.UTF8.GetString(plainBytes);
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
            catch
            {
                // 文件损坏/解密失败视为未保存
                return null;
            }
        }

        public static void ClearSavedLicense()
        {
            try
            {
                if (File.Exists(LicenseFilePath))
                    File.Delete(LicenseFilePath);
            }
            catch
            {
                // ignore
            }
        }
    }
}

