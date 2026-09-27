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
        var stored = await store.GetAllAsync(dbContextType, cancellationToken);

        if (stored.Count > 0)
            return await UnwrapAsync(stored.MaxBy(x => x.Id)!, cancellationToken);

        if (!settings.CreateRootKeyIfMissing)
            throw new EntityFrameworkEncryptionException(
                $"Root key of {dbContextType.Name} not found and creating it is disabled");

        return await CreateAsync(dbContextType, rootKeyId: 1, cancellationToken);
    }

    public async Task<RootKey?> GetRootKeyAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken)
    {
        var stored = await store.GetAllAsync(dbContextType, cancellationToken);
        var wrapped = stored.FirstOrDefault(x => x.Id == rootKeyId);

        return wrapped == null ? null : await UnwrapAsync(wrapped, cancellationToken);
    }

    public async Task<int?> GetActiveRootKeyIdAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        var stored = await store.GetAllAsync(dbContextType, cancellationToken);
        return stored.Count == 0 ? null : stored.Max(x => x.Id);
    }

    public async Task<RootKey> RotateRootKeyAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        var stored = await store.GetAllAsync(dbContextType, cancellationToken);
        var nextId = stored.Count == 0 ? 1 : stored.Max(x => x.Id) + 1;

        return await CreateAsync(dbContextType, nextId, cancellationToken);
    }

    public async Task<int> RewrapRootKeysAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        var stored = await store.GetAllAsync(dbContextType, cancellationToken);

        foreach (var wrapped in stored)
        {
            var rewrapped = await wrapper.RewrapAsync(wrapped, cancellationToken);

            if (rewrapped.Id != wrapped.Id)
                throw new EntityFrameworkEncryptionException(
                    $"Key wrapper returned root key {rewrapped.Id} when rewrapping root key {wrapped.Id} of {dbContextType.Name}");

            // the stored wrapped key is the only copy: make sure the new one can be unwrapped before replacing it
            Array.Clear(await wrapper.UnwrapAsync(rewrapped, cancellationToken));

            await store.UpdateAsync(dbContextType, rewrapped with { CreatedAt = wrapped.CreatedAt }, cancellationToken);
        }

        return stored.Count;
    }

    private async Task<RootKey> CreateAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken)
    {
        var generated = await wrapper.GenerateAsync(rootKeyId, cancellationToken);
        var wrapped = new WrappedRootKey(rootKeyId, generated.WrappingKeyId, generated.WrappedKey, timeProvider.GetUtcNow());

        if (await store.TryAddAsync(dbContextType, wrapped, cancellationToken))
            return new RootKey(rootKeyId, generated.Key);

        // another instance created a root key with this id concurrently: use theirs
        Array.Clear(generated.Key);

        return await GetRootKeyAsync(dbContextType, rootKeyId, cancellationToken)
            ?? throw new EntityFrameworkEncryptionException($"Failed to store root key {rootKeyId} of {dbContextType.Name}");
    }

    private async Task<RootKey> UnwrapAsync(WrappedRootKey wrapped, CancellationToken cancellationToken)
        => new(wrapped.Id, await wrapper.UnwrapAsync(wrapped, cancellationToken));
}
