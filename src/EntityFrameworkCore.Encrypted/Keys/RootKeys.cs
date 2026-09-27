namespace EntityFrameworkCore.Encrypted.Keys;

/// <summary>Plaintext root key. Data keys are derived from it; it only ever lives in process memory.</summary>
public sealed record RootKey(int Id, byte[] Key);

/// <summary>Root key encrypted (wrapped) by a key management service, as persisted in a <see cref="IRootKeyStore"/>.</summary>
/// <param name="WrappingKeyId">Identifier of the key that wrapped the root key, e.g. an AWS KMS key ARN.</param>
public sealed record WrappedRootKey(int Id, string WrappingKeyId, byte[] WrappedKey, DateTimeOffset CreatedAt);

/// <summary>Newly generated root key in both plaintext and wrapped form.</summary>
public sealed record GeneratedRootKey(byte[] Key, string WrappingKeyId, byte[] WrappedKey);
