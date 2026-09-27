using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using EntityFrameworkCore.Encrypted.Common.Crypto;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Keys;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.Encrypted.Common.Keys;

/// <summary>
/// Application-wide (singleton) key cache, per <see cref="Microsoft.EntityFrameworkCore.DbContext"/> type:
/// root keys from <see cref="IRootKeyProvider"/> and data keys derived from them with HKDF.
/// Only the active root key is loaded eagerly; other root keys are loaded when a value encrypted with them is read.
/// </summary>
internal sealed class DataKeyRing(IRootKeyProvider rootKeyProvider, EncryptionSettings settings, ILogger<DataKeyRing> logger) : IDisposable
{
    private readonly ConcurrentDictionary<Type, Lazy<Task<ContextKeys>>> _contexts = new();
    private readonly ConcurrentBag<Action> _onDispose = [];

    public async Task InitializeAsync(Type contextType, CancellationToken cancellationToken)
    {
        try
        {
            await GetOrStartLoad(contextType).WaitAsync(cancellationToken);
        }
        catch (EncryptionKeyStoreUnavailableException ex)
        {
            logger.LogWarning(ex,
                "Key store of {Context} is not available yet (e.g. not migrated). Keys will be loaded on first use",
                contextType.Name);
        }
    }

    public (KeyId KeyId, byte[] Key) GetEncryptionKey(Type contextType)
    {
        var keys = GetContextKeys(contextType);
        var keyId = new KeyId(keys.ActiveRootKeyId, settings.DataKeyVersion);

        return (keyId, keys.GetDataKey(keyId));
    }

    public byte[] GetDecryptionKey(Type contextType, KeyId keyId)
    {
        var keys = GetContextKeys(contextType);

        // encrypted with a root key this instance hasn't loaded: an older one, or a newer one rotated by another instance
        if (!keys.HasRootKey(keyId.RootKeyId))
            LoadRootKey(contextType, keys, keyId.RootKeyId);

        return keys.GetDataKey(keyId);
    }

    private void LoadRootKey(Type contextType, ContextKeys keys, ushort rootKeyId)
        => LoadRootKeyAsync(contextType, keys, rootKeyId).GetAwaiter().GetResult();

    private async Task LoadRootKeyAsync(Type contextType, ContextKeys keys, ushort rootKeyId)
    {
        // one load per root key: concurrent reads right after a rotation must not each call the key management service
        var load = keys.RootKeyLoads.GetOrAdd(rootKeyId, id => new Lazy<Task<RootKey?>>(
            () => Task.Run(() => rootKeyProvider.GetRootKeyAsync(contextType, id, CancellationToken.None))));

        try
        {
            var rootKey = await load.Value.ConfigureAwait(false)
                ?? throw new EntityFrameworkEncryptionException($"Root key {rootKeyId} of {contextType.Name} not found");

            if (keys.AddRootKey(rootKey))
                logger.LogInformation("Root key {RootKeyId} of {Context} loaded on demand", rootKey.Id, contextType.Name);
        }
        finally
        {
            // loaded keys are served from the cache; failed and missing ones are retried by the next read
            keys.RootKeyLoads.TryRemove(new KeyValuePair<ushort, Lazy<Task<RootKey?>>>(rootKeyId, load));
        }
    }

    /// <summary>
    /// Switches loaded contexts to root keys rotated by other instances. Costs one key store read per context;
    /// the key management service is called only when there is a new root key.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        foreach (var contextType in _contexts.Keys)
            await RefreshAsync(contextType, cancellationToken);
    }

    /// <inheritdoc cref="RefreshAsync(CancellationToken)"/>
    public async Task RefreshAsync(Type contextType, CancellationToken cancellationToken)
    {
        // not loaded yet: the first use loads the current active root key anyway
        if (!_contexts.TryGetValue(contextType, out var load) || load is not { IsValueCreated: true, Value.IsCompletedSuccessfully: true })
            return;

        var keys = load.Value.Result;

        try
        {
            var activeId = await rootKeyProvider.GetActiveRootKeyIdAsync(contextType, cancellationToken);

            if (activeId > keys.ActiveRootKeyId && activeId <= ushort.MaxValue)
                await LoadRootKeyAsync(contextType, keys, (ushort)activeId.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to check for a new root key of {Context}", contextType.Name);
        }
    }

    public async Task<int> RotateRootKeyAsync(Type contextType, CancellationToken cancellationToken)
    {
        var rootKey = await rootKeyProvider.RotateRootKeyAsync(contextType, cancellationToken);

        (await GetOrStartLoad(contextType).WaitAsync(cancellationToken)).AddRootKey(rootKey);

        logger.LogInformation("Root key {RootKeyId} of {Context} created and activated", rootKey.Id, contextType.Name);
        return rootKey.Id;
    }

    /// <summary>Registers cleanup of resources tied to this application, e.g. cached EF models.</summary>
    public void OnDispose(Action callback)
        => _onDispose.Add(callback);

    public void Dispose()
    {
        foreach (var callback in _onDispose)
            callback();

        foreach (var load in _contexts.Values)
        {
            if (load is { IsValueCreated: true, Value.IsCompletedSuccessfully: true })
                load.Value.Result.Clear();
        }

        _contexts.Clear();
    }

    private ContextKeys GetContextKeys(Type contextType)
    {
        var load = GetOrStartLoad(contextType);

        if (load.IsCompletedSuccessfully)
            return load.Result;

        logger.LogWarning(
            "Keys of {Context} were not loaded on startup and are being loaded synchronously on first use. " +
            "Run the app with a generic host or call InitializeEncryptionAsync() to load keys eagerly",
            contextType.Name);

        return load.GetAwaiter().GetResult();
    }

    private Task<ContextKeys> GetOrStartLoad(Type contextType)
    {
        var lazy = _contexts.GetOrAdd(contextType, CreateLoad);

        if (lazy.Value is { IsFaulted: false, IsCanceled: false })
            return lazy.Value;

        // failed loads are not cached: replace with a new attempt
        _contexts.TryUpdate(contextType, CreateLoad(contextType), lazy);
        return _contexts[contextType].Value;
    }

    // Task.Run: providers are async; running them off the caller's context keeps the sync path deadlock-free
    private Lazy<Task<ContextKeys>> CreateLoad(Type contextType)
        => new(() => Task.Run(async () =>
        {
            var rootKey = await rootKeyProvider.GetActiveRootKeyAsync(contextType, CancellationToken.None);
            logger.LogInformation("Root key {RootKeyId} of {Context} loaded", rootKey.Id, contextType.Name);
            return new ContextKeys(rootKey);
        }));

    private sealed class ContextKeys
    {
        private readonly ConcurrentDictionary<ushort, byte[]> _rootKeys = new();
        private readonly ConcurrentDictionary<KeyId, byte[]> _dataKeys = new();
        private readonly Lock _activeLock = new();

        public ContextKeys(RootKey activeRootKey)
            => AddRootKey(activeRootKey);

        public ushort ActiveRootKeyId { get; private set; }

        public ConcurrentDictionary<ushort, Lazy<Task<RootKey?>>> RootKeyLoads { get; } = new();

        public bool HasRootKey(ushort rootKeyId)
            => _rootKeys.ContainsKey(rootKeyId);

        /// <summary>Adds a root key; a newer one than the active becomes active.</summary>
        /// <returns><c>false</c> if the root key was already added.</returns>
        public bool AddRootKey(RootKey rootKey)
        {
            if (rootKey.Id is < 1 or > ushort.MaxValue)
                throw new EntityFrameworkEncryptionException($"Root key id must be between 1 and {ushort.MaxValue}, but was {rootKey.Id}");

            if (rootKey.Key is not { Length: Envelope.KeySize })
                throw new EntityFrameworkEncryptionException(
                    $"Root key {rootKey.Id} must be {Envelope.KeySize} bytes (AES-256), but was {rootKey.Key?.Length ?? 0}");

            // copy: keys are zeroed on dispose, the provider may share its arrays (static keys, other applications)
            var id = (ushort)rootKey.Id;

            if (!_rootKeys.TryAdd(id, (byte[])rootKey.Key.Clone()))
                return false;

            lock (_activeLock)
            {
                if (id > ActiveRootKeyId)
                    ActiveRootKeyId = id;
            }

            return true;
        }

        public byte[] GetDataKey(KeyId keyId)
            => _dataKeys.GetOrAdd(keyId, static (id, rootKeys) => HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    rootKeys[id.RootKeyId],
                    Envelope.KeySize,
                    info: Encoding.UTF8.GetBytes($"efenc:dek:v{id.DataKeyVersion}")),
                _rootKeys);

        public void Clear()
        {
            foreach (var key in _rootKeys.Values.Concat(_dataKeys.Values))
                Array.Clear(key);

            _rootKeys.Clear();
            _dataKeys.Clear();
        }
    }
}
