using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using EntityFrameworkCore.Encrypted.Common.Crypto;
using EntityFrameworkCore.Encrypted.Common.Diagnostics;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Keys;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.Encrypted.Common.Keys;

/// <summary>
/// Application-wide (singleton) key cache, per <see cref="Microsoft.EntityFrameworkCore.DbContext"/> type:
/// root keys from <see cref="IRootKeyProvider"/> and data keys derived from them with HKDF.
/// Only the active root key is loaded eagerly; other root keys are loaded when a value encrypted with them is read.
/// </summary>
internal sealed class DataKeyRing(
    IRootKeyProvider rootKeyProvider,
    EncryptionSettings settings,
    ILogger<DataKeyRing> logger,
    ILegacyDecryptor? legacyDecryptor = null) : IDisposable
{
    private readonly ConcurrentDictionary<Type, Lazy<Task<ContextKeys>>> _contexts = new();
    private readonly ConcurrentDictionary<Type, Lazy<Task<IndexKeys>>> _indexKeys = new();
    private readonly ConcurrentDictionary<Type, bool> _usesBlindIndexes = new();
    private readonly ConcurrentBag<Action> _onDispose = [];

    /// <summary>Reads stored values that aren't in the library's format, while migrating to it.</summary>
    public ILegacyDecryptor? LegacyDecryptor => legacyDecryptor;

    /// <summary>Data key version that encrypts new values: stored values never have a higher one, unless written by a newer deployment.</summary>
    public uint DataKeyVersion => settings.DataKeyVersion;

    public async Task InitializeAsync(Type contextType, CancellationToken cancellationToken)
    {
        try
        {
            await LoadAsync(contextType, cancellationToken);
        }
        catch (EncryptionKeyStoreUnavailableException ex)
        {
            logger.LogWarning(ex,
                "Key store of {Context} is not available yet (e.g. not migrated). Keys will be loaded on first use",
                contextType.Name);
        }
    }

    /// <summary>
    /// Loads the keys of a context if not loaded yet, and its blind index key if its model has blind indexes;
    /// failed loads are retried.
    /// </summary>
    /// <returns>Key that encrypts new values.</returns>
    public async Task<KeyId> LoadAsync(Type contextType, CancellationToken cancellationToken)
    {
        var keys = await GetOrStartLoad(contextType).WaitAsync(cancellationToken);

        if (_usesBlindIndexes.ContainsKey(contextType))
            await LoadIndexKeysAsync(contextType, cancellationToken);

        return new KeyId(keys.ActiveRootKeyId, settings.DataKeyVersion);
    }

    /// <summary>Called when a model with blind indexes is built, so the blind index key is loaded on startup too.</summary>
    public void UseBlindIndexes(Type contextType)
        => _usesBlindIndexes.TryAdd(contextType, true);

    public async Task LoadIndexKeysAsync(Type contextType, CancellationToken cancellationToken)
        => await GetOrStartIndexLoad(contextType).WaitAsync(cancellationToken);

    /// <summary>HMAC key of a blind indexed column, derived from the blind index key of the context.</summary>
    public byte[] GetIndexKey(Type contextType, string label)
    {
        var load = GetOrStartIndexLoad(contextType);

        if (!load.IsCompletedSuccessfully)
            logger.LogWarning(
                "Blind index key of {Context} was not loaded on startup and is being loaded synchronously on first use. " +
                "Run the app with a generic host or call InitializeEncryptionAsync() to load keys eagerly",
                contextType.Name);

        return load.GetAwaiter().GetResult().Get(label);
    }

    private Task<IndexKeys> GetOrStartIndexLoad(Type contextType)
    {
        var lazy = _indexKeys.GetOrAdd(contextType, CreateIndexLoad);

        if (lazy.Value is { IsFaulted: false, IsCanceled: false })
            return lazy.Value;

        _indexKeys.TryUpdate(contextType, CreateIndexLoad(contextType), lazy);
        return _indexKeys[contextType].Value;
    }

    private Lazy<Task<IndexKeys>> CreateIndexLoad(Type contextType)
        => new(() => Task.Run(async () =>
        {
            using var activity = Telemetry.StartActivity("index_key.load", contextType);

            try
            {
                var key = await rootKeyProvider.GetIndexKeyAsync(contextType, CancellationToken.None);

                if (key is not { Length: Envelope.KeySize })
                    throw new EntityFrameworkEncryptionException(
                        $"Blind index key of {contextType.Name} must be {Envelope.KeySize} bytes, but was {key?.Length ?? 0}");

                logger.LogInformation("Blind index key of {Context} loaded", contextType.Name);
                return new IndexKeys((byte[])key.Clone());
            }
            catch (Exception ex)
            {
                activity.SetError(ex);
                throw;
            }
        }));

    public (KeyId KeyId, DataKey Key) GetEncryptionKey(Type contextType)
    {
        var keys = GetContextKeys(contextType);
        var keyId = new KeyId(keys.ActiveRootKeyId, settings.DataKeyVersion);

        return (keyId, keys.GetDataKey(keyId));
    }

    public DataKey GetDecryptionKey(Type contextType, KeyId keyId)
    {
        var keys = GetContextKeys(contextType);

        // encrypted with a root key this instance hasn't loaded: an older one, or a newer one rotated by another instance
        if (!keys.HasRootKey(keyId.RootKeyId))
            LoadRootKey(contextType, keys, keyId.RootKeyId, "on_demand");

        return keys.GetDataKey(keyId);
    }

    private void LoadRootKey(Type contextType, ContextKeys keys, ushort rootKeyId, string trigger)
        => LoadRootKeyAsync(contextType, keys, rootKeyId, trigger).GetAwaiter().GetResult();

    private async Task LoadRootKeyAsync(Type contextType, ContextKeys keys, ushort rootKeyId, string trigger)
    {
        // one load per root key: concurrent reads right after a rotation must not each call the key management service
        var load = keys.RootKeyLoads.GetOrAdd(rootKeyId, id => new Lazy<Task<RootKey?>>(
            () => Task.Run(() => LoadTracedAsync(contextType, trigger, id, () => rootKeyProvider.GetRootKeyAsync(contextType, id, CancellationToken.None)))));

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
                await LoadRootKeyAsync(contextType, keys, (ushort)activeId.Value, "refresh");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to check for a new root key of {Context}", contextType.Name);
        }
    }

    public async Task<int> RotateRootKeyAsync(Type contextType, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("root_key.rotate", contextType);
        var rootKey = await rootKeyProvider.RotateRootKeyAsync(contextType, cancellationToken);
        activity?.SetTag("root_key.id", rootKey.Id);

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

        foreach (var load in _indexKeys.Values)
        {
            if (load is { IsValueCreated: true, Value.IsCompletedSuccessfully: true })
                load.Value.Result.Clear();
        }

        _contexts.Clear();
        _indexKeys.Clear();
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
            var rootKey = await LoadTracedAsync(contextType, "active", null,
                async () => (RootKey?)await rootKeyProvider.GetActiveRootKeyAsync(contextType, CancellationToken.None));
            logger.LogInformation("Root key {RootKeyId} of {Context} loaded", rootKey!.Id, contextType.Name);
            return new ContextKeys(rootKey);
        }));

    private static async Task<RootKey?> LoadTracedAsync(Type contextType, string trigger, int? rootKeyId, Func<Task<RootKey?>> load)
    {
        using var activity = Telemetry.StartActivity("root_key.load", contextType)?.SetTag("trigger", trigger);

        try
        {
            var rootKey = await load();
            activity?.SetTag("root_key.id", rootKey?.Id ?? rootKeyId);
            Telemetry.RecordRootKeyLoad(contextType, trigger, rootKey == null ? new KeyNotFoundException() : null);
            return rootKey;
        }
        catch (Exception ex)
        {
            activity.SetError(ex);
            Telemetry.RecordRootKeyLoad(contextType, trigger, ex);
            throw;
        }
    }

    /// <summary>Blind index key of a context and the HMAC keys of its columns derived from it.</summary>
    private sealed class IndexKeys(byte[] key)
    {
        private readonly ConcurrentDictionary<string, byte[]> _columnKeys = new();

        // per column: equal values in different columns have different blind indexes
        public byte[] Get(string label)
            => _columnKeys.GetOrAdd(label, static (x, key) => HKDF.DeriveKey(
                HashAlgorithmName.SHA256, key, Envelope.KeySize, info: Encoding.UTF8.GetBytes($"efenc:bidx:{x}")), key);

        public void Clear()
        {
            CryptographicOperations.ZeroMemory(key);

            foreach (var columnKey in _columnKeys.Values)
                CryptographicOperations.ZeroMemory(columnKey);

            _columnKeys.Clear();
        }
    }

    private sealed class ContextKeys
    {
        private readonly ConcurrentDictionary<ushort, byte[]> _rootKeys = new();
        private readonly ConcurrentDictionary<KeyId, Lazy<DataKey>> _dataKeys = new();
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

        // Lazy: a data key created by a losing GetOrAdd race would never be disposed
        public DataKey GetDataKey(KeyId keyId)
            => _dataKeys.GetOrAdd(keyId, static (id, rootKeys) => new Lazy<DataKey>(() => new DataKey(HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    rootKeys[id.RootKeyId],
                    Envelope.KeySize,
                    info: Encoding.UTF8.GetBytes($"efenc:dek:v{id.DataKeyVersion}")))),
                _rootKeys).Value;

        public void Clear()
        {
            foreach (var key in _rootKeys.Values)
                CryptographicOperations.ZeroMemory(key);

            foreach (var key in _dataKeys.Values.Where(x => x.IsValueCreated))
                key.Value.Dispose();

            _rootKeys.Clear();
            _dataKeys.Clear();
        }
    }
}
