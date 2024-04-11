using Amazon.KeyManagementService;
using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.EncryptionOptions;
using EntityFrameworkCore.Encryption.Internal.Providers.AesProvider;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encryption.AwsWrapping;

public static class DbContextEncryptionOptionsBuilderExtensions
{
    public static DbContextEncryptionOptionsBuilder UseAesWithAwsWrapping(
        this DbContextEncryptionOptionsBuilder builder,
        Action<WrappingOptions> configureOptions)
    {
        builder.Services
            .AddMemoryCache()
            .TryAddAWSService<IAmazonKeyManagementService>()
            .AddOptionsWithValidateOnStart<WrappingOptions>()
            .Configure(configureOptions.Invoke);
        
        builder
            .ConfigureEncryptionProvider(x =>
            {
                x.AddSingleton(
                    typeof(IEncryptionProvider<>).MakeGenericType(builder.SourceContextType),
                    typeof(AesEncryptionProvider<>).MakeGenericType(builder.SourceContextType));
            })
            .ConfigureKeyProvider(x =>
            {
                x.AddSingleton(
                    typeof(IKeyProvider<>).MakeGenericType(builder.SourceContextType),
                    typeof(AwsWrappingAesDataKeyProvider<>).MakeGenericType(builder.SourceContextType));
            });

        return builder;
    }
}