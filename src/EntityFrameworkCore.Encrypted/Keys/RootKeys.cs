namespace EntityFrameworkCore.Encrypted.Keys;

/// <summary>Root key encrypted (wrapped) by a key management service, as persisted in a <see cref="IRootKeyStore"/>.</summary>
/// <param name="Id">Root key id, starting at 1; <c>0</c> is the blind index key of the context.</param>
/// <param name="WrappingKeyId">Identifier of the key that wrapped the root key, e.g. an AWS KMS key ARN.</param>
/// <param name="WrappedKey">The wrapped root key, as returned by the key management service.</param>
/// <param name="CreatedAt">When the root key was created.</param>
public sealed record WrappedRootKey(int Id, string WrappingKeyId, byte[] WrappedKey, DateTimeOffset CreatedAt);

/// <summary>Newly generated root key in both plaintext and wrapped form.</summary>
/// <param name="Key">The 32-byte plaintext root key.</param>
/// <param name="WrappingKeyId">Identifier of the key that wrapped the root key, e.g. an AWS KMS key ARN.</param>
/// <param name="WrappedKey">The wrapped root key, stored in the key store.</param>
public sealed record GeneratedRootKey(byte[] Key, string WrappingKeyId, byte[] WrappedKey);
