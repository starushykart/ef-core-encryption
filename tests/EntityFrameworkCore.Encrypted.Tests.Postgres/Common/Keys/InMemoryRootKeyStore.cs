using EntityFrameworkCore.Encrypted.Keys;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;

public sealed class InMemoryRootKeyStore : IRootKeyStore
{
    private readonly Dictionary<(Type, int), WrappedRootKey> _keys = [];

    public bool Unavailable { get; set; }

    public IReadOnlyList<WrappedRootKey> Keys(Type dbContextType)
    {
        lock (_keys)
            return _keys.Where(x => x.Key.Item1 == dbContextType).Select(x => x.Value).OrderBy(x => x.Id).ToList();
    }

    public Task<IReadOnlyList<WrappedRootKey>> GetAllAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        if (Unavailable)
            throw new EncryptionKeyStoreUnavailableException("Store is not migrated", new InvalidOperationException());

        return Task.FromResult(Keys(dbContextType));
    }

    public Task<bool> TryAddAsync(Type dbContextType, WrappedRootKey rootKey, CancellationToken cancellationToken)
    {
        lock (_keys)
            return Task.FromResult(_keys.TryAdd((dbContextType, rootKey.Id), rootKey));
    }
}
