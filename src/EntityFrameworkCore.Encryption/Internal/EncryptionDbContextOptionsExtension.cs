using EntityFrameworkCore.Encryption.Common.Abstractions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encryption.Internal;

internal sealed class EncryptionDbContextOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;
    private IEncryptionProvider? _provider;

    public EncryptionDbContextOptionsExtension() {}

    private EncryptionDbContextOptionsExtension(EncryptionDbContextOptionsExtension copyFrom)
    {
        _provider = copyFrom._provider;
    }
    
    public IEncryptionProvider Provider => _provider ?? throw new InvalidOperationException("Encryption provider not set");
    public DbContextOptionsExtensionInfo Info => _info ??= new EncryptionExtensionInfo(this);
    
    private EncryptionDbContextOptionsExtension Clone() => new(this);

    public EncryptionDbContextOptionsExtension WithEncryptionProvider(IEncryptionProvider provider)
    {
        var clone = Clone();
        clone._provider = provider;
        return clone;
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
            => extension.Provider?.GetHashCode() ?? 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
            => other is EncryptionExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["Encryption:Enabled"] = "true";
    }
}