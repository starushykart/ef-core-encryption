namespace EntityFrameworkCore.Encrypted.Common.Storage;

/// <summary>Wrapped root key row of the <c>__EncryptionKeys</c> table, added to every encrypted context.</summary>
internal sealed class EncryptionKeyEntity
{
    public const string TableName = "__EncryptionKeys";

    public int Id { get; set; }
    public string WrappingKeyId { get; set; } = null!;
    public byte[] WrappedKey { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
