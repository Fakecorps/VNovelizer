using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine; // 需要引用 UnityEngine 来打 Log

public static class AESUtil
{
    // 获取配置
    private static VNProjectConfig Config => VNProjectConfig.Instance;

    // 辅助：获取合法的 Key (32位)
    private static byte[] GetKey()
    {
        string k = Config.Key;
        if (string.IsNullOrEmpty(k)) k = "DefaultKey1234567890123456789012";
        // 强制截取或补全到 32 字节
        return Encoding.UTF8.GetBytes(k.PadRight(32).Substring(0, 32));
    }

    // 辅助：获取合法的 IV (16位)
    private static byte[] GetIV()
    {
        string v = Config.IV;
        if (string.IsNullOrEmpty(v)) v = "DefaultIV1234567";
        // 强制截取或补全到 16 字节
        return Encoding.UTF8.GetBytes(v.PadRight(16).Substring(0, 16));
    }

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";

        byte[] keyBytes = GetKey();
        byte[] ivBytes = GetIV();
        byte[] inputBytes = Encoding.UTF8.GetBytes(plainText);

        try
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.IV = ivBytes;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    byte[] resultBytes = encryptor.TransformFinalBlock(inputBytes, 0, inputBytes.Length);
                    return Convert.ToBase64String(resultBytes);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[AES] 加密失败: {e.Message}");
            return plainText; // 失败则返回原文，避免数据丢失
        }
    }

    public static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return "";

        byte[] keyBytes = GetKey();
        byte[] ivBytes = GetIV();

        try
        {
            byte[] inputBytes = Convert.FromBase64String(encryptedText);

            using (Aes aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.IV = ivBytes;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    byte[] resultBytes = decryptor.TransformFinalBlock(inputBytes, 0, inputBytes.Length);
                    return Encoding.UTF8.GetString(resultBytes);
                }
            }
        }
        catch
        {
            // 解密失败（通常是因为 key 不对或者本来就没加密）
            // 返回 null，让上层逻辑去尝试按明文解析
            return null;
        }
    }
}