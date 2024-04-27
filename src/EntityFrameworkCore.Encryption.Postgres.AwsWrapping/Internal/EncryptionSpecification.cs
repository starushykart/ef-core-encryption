using EntityFrameworkCore.Encryption.OptionsExtension;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Internal;

internal class EncryptionSpecification(EncryptionType type)
{
    private byte[]? _key;
    public byte[] Key => _key ?? throw new InvalidOperationException("Uninitialized key");
    
    public EncryptionType Type => type;

    public void SetKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _key = key;
    }
}

