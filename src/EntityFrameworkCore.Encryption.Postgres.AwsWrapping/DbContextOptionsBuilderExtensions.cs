using EntityFrameworkCore.Encryption.Internal.Providers;
using EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Internal;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping;

public static class DbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseAes256Encryption(this DbContextOptionsBuilder optionsBuilder)
    {
        InMemoryKeyStorage.Register(optionsBuilder.Options.ContextType.Name, EncryptionType.AES256);
        
        var keyProvider = new InMemoryKeyProvider(optionsBuilder.Options.ContextType.Name);
        var encryptionProvider = new AesEncryptionProvider(keyProvider);

        return optionsBuilder.UseEncryption(encryptionProvider);
    }
}