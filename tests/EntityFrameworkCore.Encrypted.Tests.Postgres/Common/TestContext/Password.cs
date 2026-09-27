using System.ComponentModel.DataAnnotations;
using EntityFrameworkCore.Encrypted.Annotations;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;

public class Password
{
    public Guid Id { get; set; }

    [Encrypted]
    public string EncryptedAttribute { get; set; } = null!;
    
    public string EncryptedFluent { get; set; } = null!;

    [MaxLength(500)]
    public string Original { get; set; } = null!;

    [Encrypted]
    public byte[]? EncryptedBinary { get; set; }
}