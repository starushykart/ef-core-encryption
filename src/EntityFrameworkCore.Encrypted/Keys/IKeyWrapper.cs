namespace EntityFrameworkCore.Encrypted.Keys;

/// <summary>Generates and unwraps root keys using a key management service (envelope encryption).</summary>
public interface IKeyWrapper
{
    /// <summary>Generates a new AES-256 root key and returns it both in plaintext and wrapped.</summary>
    /// <param name="rootKeyId">Id the root key is stored with; <c>0</c> is the blind index key of the context.</param>
    Task<GeneratedRootKey> GenerateAsync(int rootKeyId, CancellationToken cancellationToken);

    /// <summary>Unwraps a stored root key with the key that wrapped it (<see cref="WrappedRootKey.WrappingKeyId"/>).</summary>
    /// <returns>The 32-byte plaintext root key.</returns>
    Task<byte[]> UnwrapAsync(WrappedRootKey rootKey, CancellationToken cancellationToken);

    /// <summary>
    /// Wraps an existing root key with the currently configured wrapping key, e.g. after moving to another KMS key.
    /// Implementations should re-encrypt inside the key management service so the plaintext root key isn't exposed.
    /// </summary>
    Task<WrappedRootKey> RewrapAsync(WrappedRootKey rootKey, CancellationToken cancellationToken);
}
