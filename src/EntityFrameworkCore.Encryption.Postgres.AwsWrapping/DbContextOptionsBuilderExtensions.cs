using EntityFrameworkCore.Encryption.Providers;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping;

public static class DbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseAes256Encryption(this DbContextOptionsBuilder optionsBuilder)
    {
        var keyProvider = new InMemoryKeyProvider(InMemoryKeyStorage.Instance, optionsBuilder.Options.ContextType.Name);
        var encryptionProvider = new Aes256EncryptionProvider(keyProvider);

        return optionsBuilder.UseEncryption(encryptionProvider);
    }
}