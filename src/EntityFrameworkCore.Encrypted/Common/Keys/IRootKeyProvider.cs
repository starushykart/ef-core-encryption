using EntityFrameworkCore.Encrypted.Common.Exceptions;

namespace EntityFrameworkCore.Encrypted.Common.Keys;

/// <summary>Plaintext root key. Data keys are derived from it; it only ever lives in process memory.</summary>
internal sealed record RootKey(int Id, byte[] Key);

/// <summary>Supplies plaintext root keys of a <see cref="Microsoft.EntityFrameworkCore.DbContext"/>: static keys or wrapped keys.</summary>
internal interface IRootKeyProvider
{
    /// <summary>Root key used to encrypt new values: the one with the highest id.</summary>
    Task<RootKey> GetActiveRootKeyAsync(Type dbContextType, CancellationToken cancellationToken);

    /// <returns>The root key, or <c>null</c> if it does not exist.</returns>
    Task<RootKey?> GetRootKeyAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken);

    /// <summary>Id of the active root key, without loading it: checked periodically to pick up keys rotated elsewhere.</summary>
    /// <returns>The id, or <c>null</c> if there is no root key yet.</returns>
    Task<int?> GetActiveRootKeyIdAsync(Type dbContextType, CancellationToken cancellationToken);

    /// <summary>Creates a new root key that becomes active.</summary>
    Task<RootKey> RotateRootKeyAsync(Type dbContextType, CancellationToken cancellationToken);

    /// <summary>
    /// Key of the blind indexes of a context, created on first use. Stable: root key rotation doesn't change it,
    /// so blind indexes stay valid. Stored like a root key with the reserved id <see cref="IndexKeyId"/>.
    /// </summary>
    Task<byte[]> GetIndexKeyAsync(Type dbContextType, CancellationToken cancellationToken)
        => throw new EntityFrameworkEncryptionException("Blind indexes are not supported by the configured key source");

    /// <summary>Id of the blind index key in the key store: root key ids start at 1.</summary>
    const int IndexKeyId = 0;

    /// <summary>Wraps all root keys (and the blind index key) with the currently configured wrapping key.</summary>
    /// <returns>Number of rewrapped root keys.</returns>
    Task<int> RewrapRootKeysAsync(Type dbContextType, CancellationToken cancellationToken);
}
