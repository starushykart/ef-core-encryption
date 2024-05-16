using EntityFrameworkCore.Encrypted.Common.Abstractions;
using EntityFrameworkCore.Encrypted.Common.Plugin;
using EntityFrameworkCore.Encrypted.Providers;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encrypted.Common;

internal sealed class EncryptionDbContextOptionsExtension(IEncryptionProvider provider) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public IEncryptionProvider Provider { get; } = provider;

    public DbContextOptionsExtensionInfo Info => _info ??= new EncryptionExtensionInfo(this);

    internal EncryptionDbContextOptionsExtension WithEncryptionProvider(IEncryptionProvider encryptionProvider) => new(encryptionProvider);

    public EncryptionType GetEncryptionType()
    {
        return Provider switch
        {
            Aes256EncryptionProvider => EncryptionType.Aes256,
            _ => EncryptionType.Custom
        };
    }

    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, EncryptionConventionPlugin>();
    }

    public void Validate(IDbContextOptions options)
    { }

    private class EncryptionExtensionInfo(EncryptionDbContextOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "Database encryption";

        public override int GetServiceProviderHashCode()
            => extension.Provider.GetHashCode();

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is EncryptionExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Encryption:Enabled"] = "true";
    }
}