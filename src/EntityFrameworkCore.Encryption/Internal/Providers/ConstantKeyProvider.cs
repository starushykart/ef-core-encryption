using System.Security.Cryptography;
using EntityFrameworkCore.Encryption.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Internal.Providers;

public class ConstantKeyProvider<TContext> : IKeyProvider<TContext> 
    where TContext : DbContext
{
    private readonly byte[] _key;

    public ConstantKeyProvider(string base64Key)
    {
        _key = Convert.FromBase64String(base64Key);
        ValidateKey(_key);
    }

    public byte[] GetDataKey() => _key;

    private static void ValidateKey(byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
    }
}