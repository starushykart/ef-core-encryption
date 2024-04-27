using EntityFrameworkCore.Encryption.ModelBuilderExtensions;

namespace EntityFrameworkCore.Encryption.IntegrationTests.Common;

public class Password
{
    public Guid Id { get; set; }
    
    [Encrypted]
    public string EncryptedAttribute { get; set; }
    
    public string EncryptedFluent { get; set; }

    public string Original { get; set; }
}