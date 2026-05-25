using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace GlassWarehouseSystem.Services.License
{
    /// <summary>
    /// 授权码验证逻辑：验签 + 硬件指纹 + 有效期。
    /// </summary>
    public static class LicenseVerifier
    {
        // TODO: 将此常量替换为你实际的 RSA 公钥（PEM 格式）
        private const string RsaPublicKeyPem = @"
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAze4VUOJBl3v2fTuCSSqN
jbEsTG2yqE5s6E6QV2TVwiX+h5FLoyKTWZdKYn/nC65WQfkTZiZECvXwxiPC+7uw
eN47wOq5Bgi5gLHZmus1bI8rxdy2pxPAw8oMTlFeauNnjvMrFmwOLwziWXxuW4nb
1uJd/ghe/Ev9jElHre0leJsz7yCM5c/w6cndxpihIYMWkWYiqYID+LOUkaFNzWpt
w0h+WJTseoWMKUK+9mGJnLmCnySBTNE6VaWxa5fT3XFTUVN7V+1TDpssW3Fuj3k3
lzqt38zxeFsmtAO4p/rIER/z2k52gW6o/unbilzx/avKq2HjFlmT/SDI32Z70U6Z
2QIDAQAB
-----END PUBLIC KEY-----
";

        /// <summary>
        /// 尝试验证授权码。
        /// </summary>
        /// <param name="licenseText">用户输入的授权码</param>
        /// <param name="payload">解析出的授权载荷（成功时返回）</param>
        /// <param name="error">失败原因（失败时返回）</param>
        public static bool TryVerify(string licenseText, out LicensePayload? payload, out string error)
        {
            payload = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(licenseText))
            {
                error = "授权码为空。";
                return false;
            }

            var parts = licenseText.Split('.');
            if (parts.Length != 2)
            {
                error = "授权码格式不正确。";
                return false;
            }

            string payloadPart = parts[0];
            string sigPart = parts[1];

            byte[] payloadBytes;
            byte[] signatureBytes;
            try
            {
                payloadBytes = Base64UrlDecode(payloadPart);
                signatureBytes = Base64UrlDecode(sigPart);
            }
            catch
            {
                error = "授权码 Base64 解码失败。";
                return false;
            }

            // 1. 验签
            if (!VerifySignature(payloadBytes, signatureBytes))
            {
                error = "授权码验签失败（可能已被篡改）。";
                return false;
            }

            // 2. 解析 payload
            LicensePayload? parsed;
            try
            {
                string json = Encoding.UTF8.GetString(payloadBytes);
                parsed = JsonConvert.DeserializeObject<LicensePayload>(json);
            }
            catch (Exception ex)
            {
                error = $"授权码内容解析失败：{ex.Message}";
                return false;
            }

            if (parsed == null || string.IsNullOrWhiteSpace(parsed.HwHash))
            {
                error = "授权码内容不完整。";
                return false;
            }

            // 3. 校验硬件指纹
            string localHwHash = HardwareFingerprint.GetHardwareHash();
            if (!string.Equals(localHwHash, parsed.HwHash, StringComparison.Ordinal))
            {
                error = "授权码不适用于当前机器（硬件不匹配）。";
                return false;
            }

            // 4. 校验有效期
            var nowUtc = DateTime.UtcNow;
            if (nowUtc > parsed.ExpiresAt.ToUniversalTime())
            {
                error = $"授权码已过期（到期时间：{parsed.ExpiresAt:u}）。";
                return false;
            }

            payload = parsed;
            return true;
        }

        private static bool VerifySignature(byte[] payloadBytes, byte[] signatureBytes)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(RsaPublicKeyPem.AsSpan());

                return rsa.VerifyData(
                    payloadBytes,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
            }
            catch
            {
                return false;
            }
        }

        private static byte[] Base64UrlDecode(string input)
        {
            string s = input.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }
    }
}

