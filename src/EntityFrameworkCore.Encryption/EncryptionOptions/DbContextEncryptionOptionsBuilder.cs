using EntityFrameworkCore.Encryption.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encryption.EncryptionOptions;

public sealed class DbContextEncryptionOptionsBuilder(
    IServiceCollection services,
    Type sourceContextType,
    Action<DbContextOptionsBuilder> sourceOptionsAction) 
{
    private bool _isKeyMetadataStorageConfigured;
    private bool _isEncryptionProviderConfigured;
    private bool _isKeyProviderConfigured;

    internal Action<DbContextOptionsBuilder> SourceContextOptionsAction => sourceOptionsAction;
    internal IServiceCollection Services => services;
    internal Type SourceContextType => sourceContextType;

    public void Build()
    {
        if (!_isEncryptionProviderConfigured )
            throw new EntityFrameworkEncryptionException($"Encryption provider not configured for {sourceContextType.Name}");
        
        if (!_isKeyProviderConfigured)
            throw new EntityFrameworkEncryptionException($"Key provider not configured for {sourceContextType.Name}");

        if (!_isKeyMetadataStorageConfigured)
            throw new EntityFrameworkEncryptionException($"Key metadata storage not configured for {sourceContextType.Name}");
    }

    internal DbContextEncryptionOptionsBuilder ConfigureEncryptionProvider(Action<IServiceCollection> configureAction)
    {
        if (_isEncryptionProviderConfigured)
            throw new EntityFrameworkEncryptionException($"Encryption provider already configured for {sourceContextType.Name}");
        
        configureAction(services);
        
        _isEncryptionProviderConfigured = true;
        
        return this;
    }
    
    internal DbContextEncryptionOptionsBuilder ConfigureKeyProvider(Action<IServiceCollection> configureAction)
    {
        if (_isKeyProviderConfigured)
            throw new EntityFrameworkEncryptionException($"Key provider already configured for {sourceContextType.Name}");
        
        configureAction(services);
        
        _isKeyProviderConfigured = true;
        
        return this;
    }
    
    internal DbContextEncryptionOptionsBuilder ConfigureKeyMetadataStorage(Action<IServiceCollection> configureAction)
    {
        if (_isKeyMetadataStorageConfigured)
            throw new EntityFrameworkEncryptionException($"Key metadata storage already configured for {sourceContextType.Name}");
        
        configureAction(services);
        
        _isKeyMetadataStorageConfigured = true;
        
        return this;
    }
}