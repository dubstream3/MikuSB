using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MikuSB.Util.Crypto;

public static class DispatchEncryption
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string? EncryptDispatchContent(string version, object? data)
    {
        if (!ConfigManager.Hotfix.AesKeys.TryGetValue(version, out var aesKey))
            return null;

        var serializedData = JsonSerializer.Serialize(data, JsonSerializerOptions);
        var keyBytes = aesKey.Split(' ')
            .Select(b => Convert.ToByte(b, 16))
            .ToArray();

        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = keyBytes;

        var encryptor = aes.CreateEncryptor();
        var dataBytes = Encoding.UTF8.GetBytes(serializedData);
        var encryptedBytes = encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);

        return Convert.ToBase64String(encryptedBytes);
    }
}