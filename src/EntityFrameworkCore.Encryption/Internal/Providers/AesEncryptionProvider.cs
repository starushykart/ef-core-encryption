using System.Security.Cryptography;
using EntityFrameworkCore.Encryption.Common.Abstractions;

namespace EntityFrameworkCore.Encryption.Internal.Providers;

public class AesEncryptionProvider(IKeyProvider keyProvider) : IEncryptionProvider
{
    public byte[]? Encrypt(byte[]? input)
    {
        if (input is null || input.Length == 0)
            return null;

        using var aes = CreateCryptographyProvider(keyProvider.GetDataKey());

        using var memoryStream = new MemoryStream();
        memoryStream.Write(aes.IV);

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write);
        cryptoStream.Write(input);
        cryptoStream.FlushFinalBlock();
        return memoryStream.ToArray();
    }

    public byte[]? Decrypt(byte[]? input)
    {
        if (input == null || input.Length == 0)
            return null;

        using var aes = CreateCryptographyProvider(keyProvider.GetDataKey());
        using var memoryStream = new MemoryStream();

        var ivSize = aes.BlockSize / 8;
        var iv = input[..ivSize];
        var data = input[ivSize..];

        using var transform = aes.CreateDecryptor(aes.Key, iv);
        using var cryptoStream = new CryptoStream(memoryStream, transform, CryptoStreamMode.Write);

        cryptoStream.Write(data);
        cryptoStream.FlushFinalBlock();
        return memoryStream.ToArray();
    }

    private static Aes CreateCryptographyProvider(byte[] key)
    {
        var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        return aes;
    }
}