using Amazon.KeyManagementService;
using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.Common.Exceptions;
using EntityFrameworkCore.Encryption.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EntityFrameworkCore.Encryption.AwsWrapping;

internal class AwsWrappingAesDataKeyProvider<TContext>(
    IMemoryCache memoryCache,
    IKeyMetadataStorage<TContext> metadataStorage,
    IAmazonKeyManagementService kmsService,
    IOptions<WrappingOptions> options) : IKeyProvider<TContext> 
    where TContext : DbContext
{
    private static readonly string ContextId = typeof(TContext).Name;

    public byte[] GetDataKey()
        => memoryCache.GetOrCreate(
            ContextId,
            cacheEntry =>
            {
                cacheEntry.SetAbsoluteExpiration(options.Value.DataKeyCacheExpiration);
                return GetKeyInternal();
            }) ?? throw new EntityFrameworkEncryptionException("Data key cannot be null");

    private byte[]? GetKeyInternal()
    {
        var metadata = metadataStorage.Get();
        
        if (metadata == null)
        {
            if (options.Value.GenerateDataKeyIfNotExist)
                return GenerateKeyInternal();

            throw new EntityFrameworkEncryptionException($"Data key for context {ContextId} was not found");
        }

        var result = kmsService.Decrypt(Convert.FromBase64String(metadata.Key), new Dictionary<string, string>());

        return result.ToArray();
    }

    private byte[]? GenerateKeyInternal()
    {
        var keyResponse = kmsService.GenerateDataKey(
            options.Value.WrappingKeyArn,
            new Dictionary<string, string>(),
            DataKeySpec.AES_256);

        metadataStorage.Create(new KeyMetadata
        {
            ContextId = ContextId,
            Key = Convert.ToBase64String(keyResponse.KeyCiphertext),
        });

        return keyResponse.KeyPlaintext;
    }
}