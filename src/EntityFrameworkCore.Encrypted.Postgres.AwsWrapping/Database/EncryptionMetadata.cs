namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;

internal class EncryptionMetadata
{
    public string ContextId { get; set; } = null!;
    public byte[] Key { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; }
    
    public void Update(byte[] key)
    {
        Key = key;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
    
    public static EncryptionMetadata Create(string contextId, byte[] key)
        => new()
        {
            ContextId = contextId,
            Key = key,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
