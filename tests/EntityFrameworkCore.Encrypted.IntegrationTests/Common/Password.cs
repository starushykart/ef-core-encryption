using EntityFrameworkCore.Encrypted.Annotations;

namespace EntityFrameworkCore.Encrypted.IntegrationTests.Common;

public class Password
{
    public Guid Id { get; set; }

    [Encrypted]
    public string EncryptedAttribute { get; set; } = null!;
    
    public string EncryptedFluent { get; set; } = null!;

    public string Original { get; set; } = null!;
}