using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.Internal;
using EntityFrameworkCore.Encryption.Internal.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Encryption;

public static class DbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseEncryption(this DbContextOptionsBuilder optionsBuilder, IEncryptionProvider encryptionProvider)
    {
        var extension = (optionsBuilder.Options.FindExtension<EncryptionDbContextOptionsExtension>() 
                         ?? new EncryptionDbContextOptionsExtension())
            .WithEncryptionProvider(encryptionProvider);

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(extension);
        
        return optionsBuilder;
    }

    public static DbContextOptionsBuilder UseAes256Encryption(this DbContextOptionsBuilder optionsBuilder, string keyBase64)
    {
        var keyProvider = new ConstantKeyProvider(keyBase64);
        var encryptionProvider = new AesEncryptionProvider(keyProvider);

        return optionsBuilder.UseEncryption(encryptionProvider);
    }
    
    public static DbContextOptionsBuilder<TContext> UseDesignTimeEncryption<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        optionsBuilder.UseEncryption(new DesignTimeEncryptionProvider());
        return optionsBuilder;
    }
}