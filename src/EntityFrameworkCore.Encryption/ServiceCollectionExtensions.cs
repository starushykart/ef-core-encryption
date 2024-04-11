using EntityFrameworkCore.Encryption.EncryptionOptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encryption;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDbContext<TContext>(this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction,
        Action<DbContextEncryptionOptionsBuilder>? encryptionAction,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
        where TContext : DbContext
    {
        var builder = new DbContextEncryptionOptionsBuilder(services, typeof(TContext), optionsAction);
        encryptionAction?.Invoke(builder);
        builder.Build();
        services.AddDbContext<TContext>(optionsAction, contextLifetime, optionsLifetime);
        
        return services;
    }
}