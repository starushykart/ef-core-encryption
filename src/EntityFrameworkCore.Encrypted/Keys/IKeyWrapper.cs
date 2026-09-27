namespace EntityFrameworkCore.Encrypted.Keys;

/// <summary>Generates and unwraps root keys using a key management service (envelope encryption).</summary>
public interface IKeyWrapper
{
    Task<GeneratedRootKey> GenerateAsync(int rootKeyId, CancellationToken cancellationToken);

    Task<byte[]> UnwrapAsync(WrappedRootKey rootKey, CancellationToken cancellationToken);

    /// <summary>
    /// Wraps an existing root key with the currently configured wrapping key, e.g. after moving to another KMS key.
    /// Implementations should re-encrypt inside the key management service so the plaintext root key isn't exposed.
    /// </summary>
    Task<WrappedRootKey> RewrapAsync(WrappedRootKey rootKey, CancellationToken cancellationToken);
}
