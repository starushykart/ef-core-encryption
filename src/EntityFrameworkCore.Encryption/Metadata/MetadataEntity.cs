namespace EntityFrameworkCore.Encryption.Metadata;

public class KeyMetadata
{
    public string ContextId { get; set; } = null!;
    public string Key { get; set; } = null!;
    public DateTimeOffset RotationToken { get; set; }
}
