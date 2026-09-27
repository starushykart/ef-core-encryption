namespace EntityFrameworkCore.Encrypted.Keys;

/// <summary>Persists wrapped root keys of a <see cref="Microsoft.EntityFrameworkCore.DbContext"/>.</summary>
public interface IRootKeyStore
{
    /// <exception cref="EncryptionKeyStoreUnavailableException">The store is not ready yet, e.g. not migrated.</exception>
    Task<IReadOnlyList<WrappedRootKey>> GetAllAsync(Type dbContextType, CancellationToken cancellationToken);

    /// <returns><c>false</c> if a root key with the same id already exists (created concurrently).</returns>
    Task<bool> TryAddAsync(Type dbContextType, WrappedRootKey rootKey, CancellationToken cancellationToken);

    /// <summary>Replaces the wrapped form of an existing root key, e.g. after rewrapping it with another wrapping key.</summary>
    Task UpdateAsync(Type dbContextType, WrappedRootKey rootKey, CancellationToken cancellationToken);
}
