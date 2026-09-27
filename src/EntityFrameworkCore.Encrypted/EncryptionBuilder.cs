using EntityFrameworkCore.Encrypted.Common.Crypto;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Common.Storage;
using EntityFrameworkCore.Encrypted.Keys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntityFrameworkCore.Encrypted;

public sealed class EncryptionBuilder
{
    private readonly Dictionary<int, byte[]> _staticKeys = [];
    private Func<IServiceProvider, IKeyWrapper>? _keyWrapperFactory;
    private Func<IServiceProvider, IRootKeyStore>? _rootKeyStoreFactory;
    private Func<IServiceProvider, IRootKeyProvider>? _rootKeyProviderFactory;
    private bool _createRootKeyIfMissing = true;
    private uint _dataKeyVersion;

    internal EncryptionBuilder(IServiceCollection services)
        => Services = services;

    public IServiceCollection Services { get; }

    /// <summary>
    /// Uses a static AES-256 root key, shared by all encrypted contexts. Call again with a higher <paramref name="id"/>
    /// to rotate: the highest id encrypts new values, the others only decrypt.
    /// </summary>
    public EncryptionBuilder UseKey(byte[] key, int id = 1)
    {
        if (key is not { Length: Envelope.KeySize })
            throw new EntityFrameworkEncryptionException(
                $"Encryption key must be {Envelope.KeySize} bytes (AES-256), but was {key?.Length ?? 0}");

        if (id is < 1 or > ushort.MaxValue)
            throw new EntityFrameworkEncryptionException($"Key id must be between 1 and {ushort.MaxValue}, but was {id}");

        // copy: the key ring zeroes its keys on dispose and must not touch the caller's array
        _staticKeys[id] = (byte[])key.Clone();
        return this;
    }

    /// <inheritdoc cref="UseKey(byte[], int)"/>
    public EncryptionBuilder UseKey(string keyBase64, int id = 1)
    {
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new EntityFrameworkEncryptionException("Encryption key can not be null or empty");

        return UseKey(Convert.FromBase64String(keyBase64), id);
    }

    /// <summary>
    /// Envelope encryption: root keys are generated and wrapped by a key management service and stored wrapped
    /// in the <c>__EncryptionKeys</c> table of each encrypted context.
    /// </summary>
    public EncryptionBuilder UseKeyWrapper(Func<IServiceProvider, IKeyWrapper> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _keyWrapperFactory = factory;
        return this;
    }

    /// <summary>Stores wrapped root keys in a custom store instead of the <c>__EncryptionKeys</c> table.</summary>
    public EncryptionBuilder UseRootKeyStore(Func<IServiceProvider, IRootKeyStore> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _rootKeyStoreFactory = factory;
        return this;
    }

    /// <summary>Supplies root keys from a custom provider.</summary>
    public EncryptionBuilder UseRootKeyProvider(Func<IServiceProvider, IRootKeyProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _rootKeyProviderFactory = factory;
        return this;
    }

    /// <summary>
    /// Creates the root key on first start when the store has none (default: <c>true</c>).
    /// Disable to require provisioned keys, e.g. when the application may only unwrap keys.
    /// </summary>
    public EncryptionBuilder CreateRootKeyIfMissing(bool value = true)
    {
        _createRootKeyIfMissing = value;
        return this;
    }

    /// <summary>
    /// Version of the data key derived from the root key for new values (default: 0). Increase it to rotate the data key:
    /// no key management calls, values encrypted with previous versions stay readable.
    /// </summary>
    public EncryptionBuilder UseDataKeyVersion(uint version)
    {
        _dataKeyVersion = version;
        return this;
    }

    internal uint DataKeyVersion => _dataKeyVersion;

    internal Func<IServiceProvider, IRootKeyProvider> BuildRootKeyProviderFactory()
    {
        var configured = (_staticKeys.Count > 0 ? 1 : 0) + (_keyWrapperFactory != null ? 1 : 0) + (_rootKeyProviderFactory != null ? 1 : 0);

        if (configured == 0)
            throw new EntityFrameworkEncryptionException("Root key is not configured. Call UseKey(...), UseKeyWrapper(...) or a key management package, e.g. UseAwsKms(...)");

        if (configured > 1)
            throw new EntityFrameworkEncryptionException("Only one root key source can be configured: UseKey(...), UseKeyWrapper(...) or UseRootKeyProvider(...)");

        if (_rootKeyProviderFactory != null)
            return _rootKeyProviderFactory;

        if (_staticKeys.Count > 0)
        {
            var provider = new StaticRootKeyProvider(new Dictionary<int, byte[]>(_staticKeys));
            return _ => provider;
        }

        Services.TryAddSingleton<DbContextRootKeyStore>();

        var wrapperFactory = _keyWrapperFactory!;
        var storeFactory = _rootKeyStoreFactory ?? (sp => sp.GetRequiredService<DbContextRootKeyStore>());
        var createIfMissing = _createRootKeyIfMissing;

        return sp => new WrappedRootKeyProvider(
            wrapperFactory(sp),
            storeFactory(sp),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            createIfMissing);
    }
}
