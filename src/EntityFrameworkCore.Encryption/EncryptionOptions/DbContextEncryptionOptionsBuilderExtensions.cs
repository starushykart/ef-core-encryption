using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.Internal.Providers;
using EntityFrameworkCore.Encryption.Internal.Providers.AesProvider;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encryption.EncryptionOptions;

public static class DbContextEncryptionOptionsBuilderExtensions
{
    public static DbContextEncryptionOptionsBuilder UseAesEncryption(this DbContextEncryptionOptionsBuilder builder,
        string base64Key)
    {
        builder.ConfigureEncryptionProvider(x =>
        {
            x.AddSingleton(
                typeof(IEncryptionProvider<>).MakeGenericType(builder.SourceContextType),
                typeof(AesEncryptionProvider<>).MakeGenericType(builder.SourceContextType));
        });

        builder.ConfigureKeyProvider(x =>
        {
            x.AddSingleton(
                typeof(IKeyProvider<>).MakeGenericType(builder.SourceContextType),
                typeof(ConstantKeyProvider<>).MakeGenericType(builder.SourceContextType));
        });

        builder.ConfigureKeyMetadataStorage(_ => { });

        return builder;
    }
}