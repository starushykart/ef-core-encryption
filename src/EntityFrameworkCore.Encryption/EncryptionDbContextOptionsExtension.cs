using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.Internal.ModelConfigurationExtensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntityFrameworkCore.Encryption;

internal sealed class EncryptionDbContextOptionsExtension(IEncryptionProvider encryptionProvider) : IDbContextOptionsExtension 
{
    public DbContextOptionsExtensionInfo Info => new EncryptionExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        services.AddSingleton(encryptionProvider);
        services.AddSingleton<IModelCustomizer, EncryptionModelCustomizer>();
        //services.Replace(new ServiceDescriptor(typeof(IModelCustomizer), typeof(EncryptionModelCustomizer)));
    }

    public void Validate(IDbContextOptions options)
    { }

    private class EncryptionExtensionInfo(IDbContextOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "Database encryption";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => true;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["Encryption:Enabled"] = "true";
        }
    }
}