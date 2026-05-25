using System;
using System.Security.Cryptography;
using System.Text;
using System.Management;

namespace GlassWarehouseSystem.Services.License
{
    /// <summary>
    /// 负责生成本机的硬件指纹。
    /// </summary>
    public static class HardwareFingerprint
    {
        /// <summary>
        /// 生成当前机器的硬件指纹：
        /// CPU 序列号 + 主板标识 + 硬盘标识 拼接后做 SHA256，再用 Base64 编码。
        /// </summary>
        public static string GetHardwareHash()
        {
            // 1. CPU 序列号
            var cpuId = HardwareIdService.GetCpuProcessorId().Trim();

            // 2. 主板标识（例如 BaseBoard 的 SerialNumber 或 Product）
            string mainBoardId = string.Empty;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber, Product FROM Win32_BaseBoard");
                using var results = searcher.Get();
                foreach (ManagementObject mo in results)
                {
                    mainBoardId = (mo["SerialNumber"] as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(mainBoardId))
                    {
                        mainBoardId = (mo["Product"] as string)?.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(mainBoardId))
                        break;
                }
            }
            catch
            {
                // 读取失败则保持为空，不影响整体指纹生成
            }

            // 3. 硬盘标识（取系统盘所在物理磁盘的 Model/SerialNumber 之一）
            string diskId = string.Empty;
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Model, SerialNumber FROM Win32_DiskDrive");
                using var results = searcher.Get();
                foreach (ManagementObject mo in results)
                {
                    diskId = (mo["SerialNumber"] as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(diskId))
                    {
                        diskId = (mo["Model"] as string)?.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(diskId))
                        break;
                }
            }
            catch
            {
                // 读取失败则保持为空
            }

            // 4. 拼接成原始指纹字符串（使用分隔符，避免简单串联歧义）
            var raw = $"{cpuId}|{mainBoardId}|{diskId}";

            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(raw);
            var hash = sha.ComputeHash(bytes);

            var hwHash = Convert.ToBase64String(hash);

            // 方便发码端使用：在控制台输出一次当前计算出的 HwHash
            try
            {
                Console.WriteLine($"Hardware HwHash (CPU|MainBoard|Disk, Base64): {hwHash}");
            }
            catch
            {
                // 某些宿主环境下可能没有控制台，忽略输出失败
            }

            return hwHash;
        }
    }
}

