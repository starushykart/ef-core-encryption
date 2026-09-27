using EntityFrameworkCore.Encrypted.Common;
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
    private ServiceDescriptor? _keyWrapper;
    private ServiceDescriptor? _rootKeyStore;
    private ServiceDescriptor? _rootKeyProvider;
    private EncryptionSettings _settings = new();

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
    public EncryptionBuilder UseKeyWrapper<TKeyWrapper>() where TKeyWrapper : class, IKeyWrapper
        => UseKeyWrapper(ServiceDescriptor.Singleton<IKeyWrapper, TKeyWrapper>());

    /// <inheritdoc cref="UseKeyWrapper{TKeyWrapper}"/>
    public EncryptionBuilder UseKeyWrapper(Func<IServiceProvider, IKeyWrapper> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return UseKeyWrapper(ServiceDescriptor.Singleton(factory));
    }

    /// <summary>Stores wrapped root keys in a custom store instead of the <c>__EncryptionKeys</c> table.</summary>
    public EncryptionBuilder UseRootKeyStore(Func<IServiceProvider, IRootKeyStore> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _rootKeyStore = ServiceDescriptor.Singleton(factory);
        return this;
    }

    /// <summary>
    /// Creates the root key on first start when the store has none (default: <c>true</c>).
    /// Disable to require provisioned keys, e.g. when the application may only unwrap keys.
    /// </summary>
    public EncryptionBuilder CreateRootKeyIfMissing(bool value = true)
    {
        _settings = _settings with { CreateRootKeyIfMissing = value };
        return this;
    }

    /// <summary>
    /// Version of the data key derived from the root key for new values (default: 0). Increase it to rotate the data key:
    /// no key management calls, values encrypted with previous versions stay readable.
    /// </summary>
    public EncryptionBuilder UseDataKeyVersion(uint version)
    {
        _settings = _settings with { DataKeyVersion = version };
        return this;
    }

    /// <summary>
    /// Checks the key store for a root key rotated by another instance every <paramref name="interval"/> (default: off).
    /// Without it an instance switches to a new root key when it reads a value encrypted with it, or on restart,
    /// so instances that only write keep using the previous root key until then. Each check is one key store read;
    /// the key management service is called only when there is a new root key.
    /// </summary>
    public EncryptionBuilder RefreshRootKeysEvery(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _settings = _settings with { RootKeyRefreshInterval = interval };
        return this;
    }

    /// <summary>Supplies root keys from a custom provider (tests).</summary>
    internal EncryptionBuilder UseRootKeyProvider(Func<IServiceProvider, IRootKeyProvider> factory)
    {
        _rootKeyProvider = ServiceDescriptor.Singleton(factory);
        return this;
    }

    internal void Register()
    {
        var configured = (_staticKeys.Count > 0 ? 1 : 0) + (_keyWrapper != null ? 1 : 0) + (_rootKeyProvider != null ? 1 : 0);

        if (configured == 0)
            throw new EntityFrameworkEncryptionException("Root key is not configured. Call UseKey(...), UseKeyWrapper(...) or a key management package, e.g. UseAwsKms(...)");

        if (configured > 1)
            throw new EntityFrameworkEncryptionException("Only one root key source can be configured: UseKey(...) or UseKeyWrapper(...)");

        Services.TryAddSingleton(_settings);

        if (_rootKeyProvider != null)
            Services.TryAdd(_rootKeyProvider);
        else if (_staticKeys.Count > 0)
            Services.TryAddSingleton<IRootKeyProvider>(new StaticRootKeyProvider(new Dictionary<int, byte[]>(_staticKeys)));
        else
        {
            Services.TryAdd(_keyWrapper!);
            Services.TryAdd(_rootKeyStore ?? ServiceDescriptor.Singleton<IRootKeyStore, DbContextRootKeyStore>());
            Services.TryAddSingleton<IRootKeyProvider, WrappedRootKeyProvider>();
        }
    }

    private EncryptionBuilder UseKeyWrapper(ServiceDescriptor descriptor)
    {
        _keyWrapper = descriptor;
        return this;
    }
}
