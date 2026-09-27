using System.ComponentModel.DataAnnotations;
using EntityFrameworkCore.Encrypted.Annotations;

namespace EntityFrameworkCore.Samples.Encryption.AwsKms.Database;

public class PasswordWithEncryption
{
    public Guid Id { get; set; }
    
    public string EncryptedFluent { get; set; } = null!;
    
    [Encrypted]
    public string EncryptedAttribute { get; set; } = null!;
    
    [MaxLength(500)]
    public string Original { get; set; } = null!;
}