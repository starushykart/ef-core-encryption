using EntityFrameworkCore.Encrypted.Common.Hosting;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Common.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntityFrameworkCore.Encrypted;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers encryption services. Enable encryption per context with
    /// <see cref="DbContextOptionsBuilderExtensions.UseEncryption(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder)"/>.
    /// </summary>
    /// <remarks>First registration wins: calling it again doesn't change the configuration.</remarks>
    public static IServiceCollection AddEncryption(this IServiceCollection services, Action<EncryptionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new EncryptionBuilder(services);
        configure(builder);
        builder.Register();

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DataKeyRing>();
        services.TryAddSingleton<EncryptedDataMaintenance>();
        services.AddHostedService<EncryptionKeyInitializer>();
        services.AddHostedService<RootKeyRefresher>();

        return services;
    }
}
