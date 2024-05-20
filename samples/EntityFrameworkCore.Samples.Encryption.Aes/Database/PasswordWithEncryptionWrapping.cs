using System.ComponentModel.DataAnnotations;
using EntityFrameworkCore.Encrypted.Annotations;

namespace EntityFrameworkCore.Samples.Encryption.Aes.Database;

public class PasswordWithEncryption
{
    public Guid Id { get; set; }
    
    [MaxLength(500)]
    public string EncryptedFluent { get; set; } = null!;
    
    [Encrypted]
    [MaxLength(500)]
    public string EncryptedAttribute { get; set; } = null!;
    
    [MaxLength(500)]
    public string Original { get; set; } = null!;
}