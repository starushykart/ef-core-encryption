using EntityFrameworkCore.Encrypted.Common.Exceptions;

namespace EntityFrameworkCore.Encrypted.AwsKms;

public sealed class AwsKmsOptions
{
    /// <summary>KMS key (ARN, id or alias) that wraps new root keys. Existing root keys unwrap with the key that wrapped them.</summary>
    public string KeyId { get; private set; } = null!;

    /// <summary>
    /// Additional KMS encryption context, e.g. <c>service = orders</c>. Logged in CloudTrail and usable in IAM conditions
    /// (<c>kms:EncryptionContext:*</c>). Changing it makes existing root keys impossible to unwrap.
    /// </summary>
    public IReadOnlyDictionary<string, string> EncryptionContext => _encryptionContext;

    private readonly Dictionary<string, string> _encryptionContext = [];

    /// <inheritdoc cref="KeyId"/>
    public AwsKmsOptions WithKeyId(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        KeyId = keyId;
        return this;
    }

    /// <inheritdoc cref="EncryptionContext"/>
    public AwsKmsOptions WithEncryptionContext(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (key.StartsWith(AwsKmsKeyWrapper.ReservedContextPrefix, StringComparison.Ordinal))
            throw new EntityFrameworkEncryptionException($"Encryption context keys starting with '{AwsKmsKeyWrapper.ReservedContextPrefix}' are reserved");

        _encryptionContext[key] = value;
        return this;
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(KeyId))
            throw new EntityFrameworkEncryptionException($"AWS KMS key is not configured. Call {nameof(WithKeyId)}(...)");
    }
}
