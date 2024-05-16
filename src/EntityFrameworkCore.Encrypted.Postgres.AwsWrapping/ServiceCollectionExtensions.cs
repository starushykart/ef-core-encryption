using Amazon.KeyManagementService;
using EntityFrameworkCore.Encrypted.Common.Abstractions;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Internal;
using EntityFrameworkCore.Encrypted.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAwsAesDataKeyWrapping(
        this IServiceCollection services,
        string connectionString,
        Action<WrappingOptions> awsWrappingOptionsAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        
        services
            .AddOptionsWithValidateOnStart<WrappingOptions>()
            .Configure(awsWrappingOptionsAction.Invoke);

        services.TryAddSingleton<IKeyStorage>(InMemoryKeyStorage.Instance);
        services.TryAddAWSService<IAmazonKeyManagementService>();
        services.AddHostedService<AwsKeyWrappingHostedService>();
        services.AddDbContextFactory<EncryptionMetadataContext>(x => x.UseNpgsql(connectionString));

        return services;
    }

    public static IServiceCollection AddDbContext<TContext>(this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction,
        Action<WrappingOptions> awsWrappingOptionsAction,
        ServiceLifetime contextLifetime = ServiceLifetime.Scoped,
        ServiceLifetime optionsLifetime = ServiceLifetime.Scoped)
        where TContext : DbContext
    {
        services.AddAwsAesDataKeyWrapping(Utils.GetConnectionString(optionsAction), awsWrappingOptionsAction);
        services.AddDbContext<TContext>(optionsAction, contextLifetime, optionsLifetime);

        return services;
    }
    
    public static IServiceCollection AddDbContextFactory<TContext>(this IServiceCollection services,
        Action<DbContextOptionsBuilder> optionsAction,
        Action<WrappingOptions> awsWrappingOptionsAction,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TContext : DbContext
    {
        services.AddAwsAesDataKeyWrapping(Utils.GetConnectionString(optionsAction), awsWrappingOptionsAction);
        services.AddDbContextFactory<TContext>(optionsAction, lifetime);

        return services;
    }
}