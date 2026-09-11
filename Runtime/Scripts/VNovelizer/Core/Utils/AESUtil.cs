using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine; // 需要引用 UnityEngine 来打 Log

public static class AESUtil
{
    // 获取配置
    private static VNProjectConfig Config => VNProjectConfig.Instance;

    /// <summary>
    /// 【Fix-35】密钥/IV 不再使用硬编码默认值兜底：未配置时抛异常，由上层（SaveManager）
    /// 显式报错并拒绝落盘，杜绝"开着加密开关却用众所周知的默认密钥"或"静默降级明文"。
    /// </summary>
    private static byte[] GetKey()
    {
        string k = Config.Key;
        if (string.IsNullOrEmpty(k))
            throw new InvalidOperationException("[AES] 未配置密钥，拒绝加密（请到 Project Settings → VNovelizer 配置 Key/IV）");
        // 强制截取或补全到 32 字节
        return Encoding.UTF8.GetBytes(k.PadRight(32).Substring(0, 32));
    }

    /// <summary>获取配置 IV（仅用于解密旧格式存档的回退；新格式加密时使用随机 IV）。</summary>
    private static byte[] GetConfigIV()
    {
        string v = Config.IV;
        if (string.IsNullOrEmpty(v))
            throw new InvalidOperationException("[AES] 未配置 IV，拒绝加密（请到 Project Settings → VNovelizer 配置 Key/IV）");
        // 强制截取或补全到 16 字节
        return Encoding.UTF8.GetBytes(v.PadRight(16).Substring(0, 16));
    }

    /// <summary>
    /// 【Fix-35】加密改为随机 IV：输出格式 "base64(IV):base64(密文)"，相同明文不再产生相同密文，
    /// 消除差分分析风险；解密兼容无前缀的旧格式（回退到配置 IV）。
    /// 【Fix-34】不再吞异常返回明文：失败显式上抛，由 SaveManager 的 try-catch 兜底拒绝落盘。
    /// </summary>
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";

        byte[] keyBytes = GetKey();
        byte[] inputBytes = Encoding.UTF8.GetBytes(plainText);

        using (Aes aes = Aes.Create())
        {
            aes.Key = keyBytes;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // 每次加密随机生成 IV（不依赖配置 IV，配置 IV 仅作旧档解密回退）
            byte[] ivBytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(ivBytes);
            }
            aes.IV = ivBytes;

            using (ICryptoTransform encryptor = aes.CreateEncryptor())
            {
                byte[] resultBytes = encryptor.TransformFinalBlock(inputBytes, 0, inputBytes.Length);
                return Convert.ToBase64String(ivBytes) + ":" + Convert.ToBase64String(resultBytes);
            }
        }
    }

    public static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return "";

        try
        {
            byte[] keyBytes = GetKey();
            byte[] ivBytes;
            byte[] cipherBytes;

            int sepIdx = encryptedText.IndexOf(':');
            if (sepIdx > 0)
            {
                // 新格式：base64(IV):base64(密文)
                ivBytes = Convert.FromBase64String(encryptedText.Substring(0, sepIdx));
                cipherBytes = Convert.FromBase64String(encryptedText.Substring(sepIdx + 1));
            }
            else
            {
                // 旧格式兼容：整体密文 + 配置 IV
                ivBytes = GetConfigIV();
                cipherBytes = Convert.FromBase64String(encryptedText);
            }

            using (Aes aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.IV = ivBytes;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    byte[] resultBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                    return Encoding.UTF8.GetString(resultBytes);
                }
            }
        }
        catch (Exception e)
        {
            // 解密失败（key 不对 / 本来就没加密 / 格式非法），返回 null 让上层尝试按明文解析
            Debug.LogWarning($"[AES] 解密失败: {e.Message}");
            return null;
        }
    }
}