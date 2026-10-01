using EntityFrameworkCore.Encrypted.Common.Diagnostics;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Keys;

namespace EntityFrameworkCore.Encrypted.Common.Keys;

/// <summary>
/// Envelope encryption: root keys are generated and wrapped by a key management service (<see cref="IKeyWrapper"/>)
/// and persisted wrapped in a <see cref="IRootKeyStore"/>. Loading a root key costs one unwrap call.
/// </summary>
internal sealed class WrappedRootKeyProvider(IKeyWrapper wrapper, IRootKeyStore store, TimeProvider timeProvider, EncryptionSettings settings)
    : IRootKeyProvider
{
    public async Task<RootKey> GetActiveRootKeyAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        var stored = await GetRootKeysAsync(dbContextType, cancellationToken);

        if (stored.Count > 0)
            return await UnwrapAsync(dbContextType, stored.MaxBy(x => x.Id)!, cancellationToken);

        if (!settings.CreateRootKeyIfMissing)
            throw new EntityFrameworkEncryptionException(
                $"Root key of {dbContextType.Name} not found and creating it is disabled");

        return await CreateAsync(dbContextType, rootKeyId: 1, cancellationToken);
    }

    public async Task<RootKey?> GetRootKeyAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken)
    {
        var stored = await store.GetAllAsync(dbContextType, cancellationToken);
        var wrapped = stored.FirstOrDefault(x => x.Id == rootKeyId);

        return wrapped == null ? null : await UnwrapAsync(dbContextType, wrapped, cancellationToken);
    }

    public async Task<int?> GetActiveRootKeyIdAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        var stored = await GetRootKeysAsync(dbContextType, cancellationToken);
        return stored.Count == 0 ? null : stored.Max(x => x.Id);
    }

    public async Task<RootKey> RotateRootKeyAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        var stored = await GetRootKeysAsync(dbContextType, cancellationToken);
        var nextId = stored.Count == 0 ? 1 : stored.Max(x => x.Id) + 1;

        // stored in 2 bytes of every value
        if (nextId > ushort.MaxValue)
            throw new EntityFrameworkEncryptionException(
                $"{dbContextType.Name} already has the maximum number of root keys ({ushort.MaxValue})");

        return await CreateAsync(dbContextType, nextId, cancellationToken);
    }

    public async Task<byte[]> GetIndexKeyAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        if (await GetRootKeyAsync(dbContextType, IRootKeyProvider.IndexKeyId, cancellationToken) is { } existing)
            return existing.Key;

        if (!settings.CreateRootKeyIfMissing)
            throw new EntityFrameworkEncryptionException(
                $"Blind index key of {dbContextType.Name} not found and creating it is disabled");

        return (await CreateAsync(dbContextType, IRootKeyProvider.IndexKeyId, cancellationToken)).Key;
    }

    // the blind index key is stored next to the root keys and wrapped the same way, but isn't one of them
    private async Task<List<WrappedRootKey>> GetRootKeysAsync(Type dbContextType, CancellationToken cancellationToken)
        => (await store.GetAllAsync(dbContextType, cancellationToken)).Where(x => x.Id != IRootKeyProvider.IndexKeyId).ToList();

    public async Task<int> RewrapRootKeysAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("root_key.rewrap", dbContextType);
        var stored = await store.GetAllAsync(dbContextType, cancellationToken);

        foreach (var wrapped in stored)
        {
            var rewrapped = await Telemetry.KeyWrapperAsync(Telemetry.Operations.Rewrap, dbContextType, wrapped.Id,
                () => wrapper.RewrapAsync(wrapped, cancellationToken));

            if (rewrapped.Id != wrapped.Id)
                throw new EntityFrameworkEncryptionException(
                    $"Key wrapper returned root key {rewrapped.Id} when rewrapping root key {wrapped.Id} of {dbContextType.Name}");

            // the stored wrapped key is the only copy: make sure the new one can be unwrapped before replacing it
            Array.Clear(await Telemetry.KeyWrapperAsync(Telemetry.Operations.Unwrap, dbContextType, rewrapped.Id,
                () => wrapper.UnwrapAsync(rewrapped, cancellationToken)));

            await store.UpdateAsync(dbContextType, rewrapped with { CreatedAt = wrapped.CreatedAt }, cancellationToken);
        }

        return stored.Count;
    }

    private async Task<RootKey> CreateAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken)
    {
        var generated = await Telemetry.KeyWrapperAsync(Telemetry.Operations.Generate, dbContextType, rootKeyId,
            () => wrapper.GenerateAsync(rootKeyId, cancellationToken));
        var wrapped = new WrappedRootKey(rootKeyId, generated.WrappingKeyId, generated.WrappedKey, timeProvider.GetUtcNow());

        if (await store.TryAddAsync(dbContextType, wrapped, cancellationToken))
            return new RootKey(rootKeyId, generated.Key);

        // another instance created a root key with this id concurrently: use theirs
        Array.Clear(generated.Key);

        return await GetRootKeyAsync(dbContextType, rootKeyId, cancellationToken)
            ?? throw new EntityFrameworkEncryptionException($"Failed to store root key {rootKeyId} of {dbContextType.Name}");
    }

    private async Task<RootKey> UnwrapAsync(Type dbContextType, WrappedRootKey wrapped, CancellationToken cancellationToken)
        => new(wrapped.Id, await Telemetry.KeyWrapperAsync(Telemetry.Operations.Unwrap, dbContextType, wrapped.Id,
            () => wrapper.UnwrapAsync(wrapped, cancellationToken)));
}
