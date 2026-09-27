namespace EntityFrameworkCore.Encrypted.Keys;

/// <summary>Supplies plaintext root keys of a <see cref="Microsoft.EntityFrameworkCore.DbContext"/>.</summary>
public interface IRootKeyProvider
{
    /// <summary>Root key used to encrypt new values: the one with the highest id.</summary>
    Task<RootKey> GetActiveRootKeyAsync(Type dbContextType, CancellationToken cancellationToken);

    /// <returns>The root key, or <c>null</c> if it does not exist.</returns>
    Task<RootKey?> GetRootKeyAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken);

    /// <summary>Creates a new root key that becomes active.</summary>
    Task<RootKey> RotateRootKeyAsync(Type dbContextType, CancellationToken cancellationToken);
}
