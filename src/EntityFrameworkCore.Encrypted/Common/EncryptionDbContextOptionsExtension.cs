using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Common.Plugin;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntityFrameworkCore.Encrypted.Common;

/// <param name="keyRing">
/// Application key ring, or <c>null</c> when the context is created outside of DI (design time, manual options).
/// The model is still built with encrypted columns; only encrypting/decrypting values fails.
/// </param>
internal sealed class EncryptionDbContextOptionsExtension(DataKeyRing? keyRing) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DataKeyRing? KeyRing { get; } = keyRing;

    public DbContextOptionsExtensionInfo Info => _info ??= new EncryptionExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, EncryptionConventionPlugin>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInterceptor, EncryptedQueryGuard>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInterceptor, BlindIndexSaveChangesInterceptor>());

        DecorateModelCacheKeyFactory(services);
    }

    // a model passed with UseModel (e.g. a compiled model) isn't built with the encryption conventions:
    // its encrypted properties would be stored in plaintext
    public void Validate(IDbContextOptions options)
    {
        if (options.FindExtension<CoreOptionsExtension>()?.Model != null)
            throw new EntityFrameworkEncryptionException(
                "UseEncryption() can't be combined with UseModel(...), e.g. a compiled model: encrypted properties are " +
                "configured when the model is built");
    }

    private static void DecorateModelCacheKeyFactory(IServiceCollection services)
    {
        // provider extensions may register core services before or after this extension, so wrap whatever is there
        // and fall back to the EF default; a later TryAdd of the default is then a no-op
        var inner = services.LastOrDefault(x => x.ServiceType == typeof(IModelCacheKeyFactory));

        if (inner?.ImplementationType == typeof(EncryptionModelCacheKeyFactory))
            return;

        services.RemoveAll<IModelCacheKeyFactory>();
        services.AddSingleton<IModelCacheKeyFactory>(sp => new EncryptionModelCacheKeyFactory(CreateInner(sp, inner), sp.GetRequiredService<IMemoryCache>()));
    }

    private static IModelCacheKeyFactory CreateInner(IServiceProvider sp, ServiceDescriptor? descriptor)
        => descriptor switch
        {
            null => ActivatorUtilities.CreateInstance<ModelCacheKeyFactory>(sp),
            { ImplementationInstance: IModelCacheKeyFactory instance } => instance,
            { ImplementationFactory: not null } => (IModelCacheKeyFactory)descriptor.ImplementationFactory(sp),
            _ => (IModelCacheKeyFactory)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!)
        };

    private sealed class EncryptionExtensionInfo(EncryptionDbContextOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "Encryption ";

        // the key ring is not part of the internal service provider identity: all applications share one
        // (EF throws after 20 internal service providers); models are isolated via EncryptionModelCacheKeyFactory
        public override int GetServiceProviderHashCode()
            => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is EncryptionExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Encryption:Enabled"] = "true";
    }
}
