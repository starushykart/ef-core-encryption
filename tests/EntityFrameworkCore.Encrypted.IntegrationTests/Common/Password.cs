using System.ComponentModel.DataAnnotations;
using EntityFrameworkCore.Encrypted.Annotations;

namespace EntityFrameworkCore.Encrypted.IntegrationTests.Common;

public class Password
{
    public Guid Id { get; set; }

    [Encrypted]
    [MaxLength(100)]
    public string EncryptedAttribute { get; set; } = null!;
    
    [MaxLength(500)]
    public string EncryptedFluent { get; set; } = null!;

    [MaxLength(500)]
    public string Original { get; set; } = null!;
}