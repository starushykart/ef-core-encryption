using EntityFrameworkCore.Encryption.ModelBuilderExtensions;

namespace EntityFrameworkCore.Samples.Encryption.AesWrapping.Database;

public class PasswordWithEncryptionWrapping
{
    public Guid Id { get; set; }
    
    public string EncryptedFluent { get; set; } = null!;
    
    [Encrypted]
    public string EncryptedAttribute { get; set; } = null!;
    public string Original { get; set; } = null!;
}