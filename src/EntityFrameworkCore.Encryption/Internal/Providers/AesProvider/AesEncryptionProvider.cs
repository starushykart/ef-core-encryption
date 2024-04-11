using System.Security.Cryptography;
using EntityFrameworkCore.Encryption.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Internal.Providers.AesProvider;

public class AesEncryptionProvider<TContext>(IKeyProvider<TContext> keyProvider) : IEncryptionProvider<TContext>
    where TContext : DbContext
{
    public byte[]? Encrypt(byte[]? input)
    {
        if (input is null || input.Length == 0)
            return null;

        var key = keyProvider.GetDataKey();
        using var aes = CreateCryptographyProvider(key);
        using var memoryStream = new MemoryStream();

        memoryStream.Write(aes.IV);

        using var transform = aes.CreateEncryptor(key, aes.IV);
        using var cryptoStream = new CryptoStream(memoryStream, transform, CryptoStreamMode.Write);

        cryptoStream.Write(input);
        cryptoStream.FlushFinalBlock();
        return memoryStream.ToArray();
    }

    public byte[]? Decrypt(byte[]? input)
    {
        if (input is null || input.Length == 0)
            return null;

        var key = keyProvider.GetDataKey();
        using var aes = CreateCryptographyProvider(key);
        using var memoryStream = new MemoryStream();
        
        var ivSize = aes.BlockSize / 8;
        var iv = input[..ivSize];
        var data = input[ivSize..];

        using var transform = aes.CreateDecryptor(key, iv);
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