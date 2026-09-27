using EntityFrameworkCore.Encrypted.Common.Hosting;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Keys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntityFrameworkCore.Encrypted;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers encryption services. Enable encryption per context with
    /// <see cref="DbContextOptionsBuilderExtensions.UseEncryption(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder)"/>.
    /// </summary>
    public static IServiceCollection AddEncryption(this IServiceCollection services, Action<EncryptionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new EncryptionBuilder(services);
        configure(builder);

        var rootKeyProviderFactory = builder.BuildRootKeyProviderFactory();

        // last registration wins, so AddEncryption can be called more than once
        services.Replace(ServiceDescriptor.Singleton(rootKeyProviderFactory));
        services.Replace(ServiceDescriptor.Singleton(sp => new DataKeyRing(
            sp.GetRequiredService<IRootKeyProvider>(),
            builder.DataKeyVersion,
            sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(DataKeyRing)) ?? NullLogger.Instance)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EncryptionKeyInitializer>());

        return services;
    }
}
