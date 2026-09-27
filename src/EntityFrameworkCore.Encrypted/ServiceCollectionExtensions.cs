using EntityFrameworkCore.Encrypted.Common.Abstractions;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Hosting;
using EntityFrameworkCore.Encrypted.Common.Keys;
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

        var sourceFactory = builder.DataKeySourceFactory
            ?? throw new EntityFrameworkEncryptionException(
                "Data key source is not configured. Call UseKey(...) or UseDataKeySource(...)");

        // last registration wins, so AddEncryption can be called more than once (e.g. by provider packages)
        services.Replace(ServiceDescriptor.Singleton(sourceFactory));
        services.TryAddSingleton(sp => new DataKeyRing(
            sp.GetRequiredService<IDataKeySource>(),
            sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(DataKeyRing)) ?? NullLogger.Instance));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EncryptionKeyInitializer>());

        return services;
    }
}
