using System.ComponentModel.DataAnnotations;
using EntityFrameworkCore.Encrypted.Annotations;

namespace EntityFrameworkCore.Samples.Encryption.AesWrapping.Database;

public class PasswordWithEncryptionWrapping
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