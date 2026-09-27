using System.Collections.Concurrent;
using EntityFrameworkCore.Encrypted.Common.Abstractions;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.Encrypted.Common.Keys;

/// <summary>
/// Application-wide (singleton) cache of data keys, one per <see cref="Microsoft.EntityFrameworkCore.DbContext"/> type.
/// Keys are loaded lazily from <see cref="IDataKeySource"/> or eagerly on host start.
/// </summary>
internal sealed class DataKeyRing(IDataKeySource source, ILogger logger) : IDisposable
{
    internal const int KeySize = 32;

    private readonly ConcurrentDictionary<Type, Lazy<Task<byte[]>>> _keys = new();
    private readonly ConcurrentBag<Action> _onDispose = [];

    /// <summary>Registers cleanup of resources tied to this application, e.g. cached EF models.</summary>
    public void OnDispose(Action callback)
        => _onDispose.Add(callback);

    public void Dispose()
    {
        foreach (var callback in _onDispose)
            callback();

        foreach (var load in _keys.Values)
        {
            if (load is { IsValueCreated: true, Value.IsCompletedSuccessfully: true })
                Array.Clear(load.Value.Result);
        }

        _keys.Clear();
    }

    public Task InitializeAsync(Type contextType, CancellationToken cancellationToken)
        => GetOrStartLoad(contextType).WaitAsync(cancellationToken);

    public byte[] GetKey(Type contextType)
    {
        var load = GetOrStartLoad(contextType);

        if (load.IsCompletedSuccessfully)
            return load.Result;

        logger.LogWarning(
            "Data key for {Context} was not loaded on startup and is being loaded synchronously on first use. " +
            "Run the app with a generic host or call InitializeEncryptionAsync() to load keys eagerly",
            contextType.Name);

        return load.GetAwaiter().GetResult();
    }

    private Task<byte[]> GetOrStartLoad(Type contextType)
    {
        var lazy = _keys.GetOrAdd(contextType, CreateLoad);

        if (lazy.Value is { IsFaulted: false, IsCanceled: false })
            return lazy.Value;

        // failed loads are not cached: replace with a new attempt
        _keys.TryUpdate(contextType, CreateLoad(contextType), lazy);
        return _keys[contextType].Value;
    }

    // Task.Run: sources are async; running them off the caller's context keeps the sync path deadlock-free
    private Lazy<Task<byte[]>> CreateLoad(Type contextType)
        => new(() => Task.Run(() => LoadAsync(contextType)));

    private async Task<byte[]> LoadAsync(Type contextType)
    {
        var key = await source.GetDataKeyAsync(new DataKeyContext(contextType), CancellationToken.None);

        if (key is not { Length: KeySize })
            throw new EntityFrameworkEncryptionException(
                $"Data key for {contextType.Name} must be {KeySize} bytes (AES-256), but was {key?.Length ?? 0}");

        logger.LogInformation("Data encryption key for {Context} loaded", contextType.Name);
        return key;
    }
}
