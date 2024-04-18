using EntityFrameworkCore.Encryption.Internal.ModelConfigurationExtensions;

namespace EntityFrameworkCore.Samples.Encryption.Models;

public class Password
{
    public Guid Id { get; set; }
    
    public string EncryptedFluent { get; set; } = null!;
    
    [Encrypted]
    public string EncryptedAttribute { get; set; } = null!;
    public string Original { get; set; } = null!;
}