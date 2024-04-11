using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.Internal;
using EntityFrameworkCore.Encryption.Internal.Providers.DesignTimeProvider;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Encryption;

public abstract class EncryptedDbContext(DbContextOptions options) : DbContext(options)
{
    private bool _isDesignTime;

    public void IsDesignTime(bool value)
        => _isDesignTime = value;
    
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        var encryptionProvider = _isDesignTime
            ? new DesignTimeEncryptionProvider()
            : (IEncryptionProvider)this.GetService(typeof(IEncryptionProvider<>).MakeGenericType(GetType()));
        
        configurationBuilder.Conventions.Add(x => new EncryptionConvention(encryptionProvider));
    }
}