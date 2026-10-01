using EntityFrameworkCore.Encrypted.Common;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encrypted;

/// <summary>Enables encryption for a context.</summary>
public static class DbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Enables encryption of properties marked with <c>[Encrypted]</c> or <c>IsEncrypted()</c>,
    /// using services registered by <see cref="ServiceCollectionExtensions.AddEncryption"/>.
    /// </summary>
    /// <remarks>
    /// Outside of dependency injection (design-time factories, manually built options) the model is still
    /// configured with encrypted columns, but encrypting or decrypting values throws.
    /// </remarks>
    public static DbContextOptionsBuilder UseEncryption(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        // EF sets the application service provider before invoking the AddDbContext* options action
        var applicationServiceProvider = optionsBuilder.Options
            .FindExtension<CoreOptionsExtension>()?
            .ApplicationServiceProvider;

        var keyRing = applicationServiceProvider?.GetService<DataKeyRing>();

        if (applicationServiceProvider != null && keyRing == null)
            throw new EntityFrameworkEncryptionException(
                $"Encryption services are not registered. Call services.{nameof(ServiceCollectionExtensions.AddEncryption)}(...)");

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new EncryptionDbContextOptionsExtension(keyRing));

        return optionsBuilder;
    }

    /// <inheritdoc cref="UseEncryption(DbContextOptionsBuilder)"/>
    public static DbContextOptionsBuilder<TContext> UseEncryption<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseEncryption((DbContextOptionsBuilder)optionsBuilder);
}
