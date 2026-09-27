using EntityFrameworkCore.Encrypted.Common.Abstractions;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntityFrameworkCore.Encrypted;

public sealed class EncryptionBuilder
{
    internal EncryptionBuilder(IServiceCollection services)
        => Services = services;

    public IServiceCollection Services { get; }

    internal Func<IServiceProvider, IDataKeySource>? DataKeySourceFactory { get; private set; }

    /// <summary>Uses one static AES-256 key for all encrypted contexts.</summary>
    public EncryptionBuilder UseKey(byte[] key)
    {
        if (key is not { Length: DataKeyRing.KeySize })
            throw new EntityFrameworkEncryptionException(
                $"Encryption key must be {DataKeyRing.KeySize} bytes (AES-256), but was {key?.Length ?? 0}");

        var source = new ConstantDataKeySource(key);
        return UseDataKeySource(_ => source);
    }

    /// <summary>Uses one static AES-256 key for all encrypted contexts.</summary>
    public EncryptionBuilder UseKey(string keyBase64)
    {
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new EntityFrameworkEncryptionException("Encryption key can not be null or empty");

        return UseKey(Convert.FromBase64String(keyBase64));
    }

    /// <summary>Resolves data keys from a custom source, e.g. a key management service.</summary>
    public EncryptionBuilder UseDataKeySource<TSource>() where TSource : class, IDataKeySource
    {
        Services.TryAddSingleton<TSource>();
        return UseDataKeySource(sp => sp.GetRequiredService<TSource>());
    }

    /// <summary>Resolves data keys from a custom source, e.g. a key management service.</summary>
    public EncryptionBuilder UseDataKeySource(Func<IServiceProvider, IDataKeySource> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        DataKeySourceFactory = factory;
        return this;
    }
}
