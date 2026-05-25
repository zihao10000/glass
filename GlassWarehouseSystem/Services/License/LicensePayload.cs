using System;

namespace GlassWarehouseSystem.Services.License
{
    /// <summary>
    /// 授权信息载荷，由发码工具生成并签名。
    /// </summary>
    public class LicensePayload
    {
        /// <summary>
        /// 硬件指纹哈希（例如 CPU 序列号的 SHA256，Base64 编码）
        /// </summary>
        public string HwHash { get; set; } = default!;

        /// <summary>
        /// 授权到期时间（UTC）
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// 可选：授权 ID / 客户标识等
        /// </summary>
        public string? LicenseId { get; set; }
    }
}

