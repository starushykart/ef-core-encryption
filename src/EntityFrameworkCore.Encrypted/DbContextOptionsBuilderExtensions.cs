using EntityFrameworkCore.Encrypted.Common;
using EntityFrameworkCore.Encrypted.Common.Abstractions;
using EntityFrameworkCore.Encrypted.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Encrypted;

public static class DbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseAes256Encryption(this DbContextOptionsBuilder optionsBuilder, byte[] key)
    {
        InMemoryKeyStorage.Instance.AddKey(optionsBuilder.Options.ContextType.Name, key);

        var keyProvider = new InMemoryKeyProvider(InMemoryKeyStorage.Instance, optionsBuilder.Options.ContextType.Name);
        var encryptionProvider = new Aes256EncryptionProvider(keyProvider);

        return optionsBuilder.UseEncryption(encryptionProvider);
    }
    
    public static DbContextOptionsBuilder UseAes256Encryption(this DbContextOptionsBuilder optionsBuilder, string keyBase64) 
        => optionsBuilder.UseAes256Encryption(Convert.FromBase64String(keyBase64));
    
    public static DbContextOptionsBuilder UseEncryption(this DbContextOptionsBuilder optionsBuilder, IEncryptionProvider encryptionProvider)
    {
        var extension = (optionsBuilder.Options.FindExtension<EncryptionDbContextOptionsExtension>()
                         ?? new EncryptionDbContextOptionsExtension(encryptionProvider))
            .WithEncryptionProvider(encryptionProvider);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        
        return optionsBuilder;
    }
    
    public static DbContextOptionsBuilder<TContext> UseDesignTimeEncryption<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        optionsBuilder.UseEncryption(new DesignTimeEncryptionProvider());
        return optionsBuilder;
    }
}