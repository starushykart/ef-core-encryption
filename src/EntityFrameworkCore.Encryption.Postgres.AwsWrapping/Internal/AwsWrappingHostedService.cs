using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using EntityFrameworkCore.Encryption.Common;
using EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Internal;

internal class AwsKeyWrappingHostedService(
    IAmazonKeyManagementService kmsService,
    IDbContextFactory<EncryptionMetadataContext> dbContextFactory,
    IOptions<WrappingOptions> options,
    ILogger<AwsKeyWrappingHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            foreach (var specification in InMemoryKeyStorage.Specifications)
            {
                var metadata = await context.Metadata
                    .FirstOrDefaultAsync(x => x.ContextId == specification.Key, cancellationToken);

                if (metadata == null && !options.Value.GenerateDataKeyIfNotExist)
                {
                    throw new EntityFrameworkEncryptionException(
                        $"Data encryption key for {specification.Key} not found");
                }

                byte[] dataKey;

                if (metadata != null)
                {
                    dataKey = await DecryptAsync(metadata.Key, cancellationToken);
                    var reEncryptedDataKey = await EncryptAsync(dataKey, cancellationToken);

                    await VerifyAsync(dataKey, reEncryptedDataKey, cancellationToken);

                    metadata.Update(reEncryptedDataKey);
                    await context.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    var (encrypted, decrypted) =
                        await GenerateDataKeyAsync(specification.Value.Type, cancellationToken);

                    dataKey = decrypted;

                    context.Metadata.Add(EncryptionMetadata.Create(specification.Key, encrypted));

                    await context.SaveChangesAsync(cancellationToken);
                }

                specification.Value.SetKey(dataKey);
                logger.LogInformation("Data encryption key for context {ContextType} initialized", specification.Key);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error occured during database encryption initialization");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
    
    private async Task VerifyAsync(byte[] decryptedKey, byte[] reEncryptedDataKey, CancellationToken ct)
    {
        var verificationDecryptedKey = await DecryptAsync(reEncryptedDataKey, ct);
        var verified = decryptedKey.SequenceEqual(verificationDecryptedKey);

        if (!verified)
            throw new EntityFrameworkEncryptionException("Original data key not the same as re-encrypted");
    }
    
    private async Task<byte[]> DecryptAsync(byte[] key, CancellationToken ct)
    {
        var decryptRequest = new DecryptRequest
        {
            CiphertextBlob = new MemoryStream(key),
            KeyId = options.Value.WrappingKeyArn,
        };
        var response = await kmsService.DecryptAsync(decryptRequest, ct);
        
        var result = response.Plaintext.ToArray();
        await response.Plaintext.DisposeAsync();
        
        return result;
    }
    
    private async Task<byte[]> EncryptAsync(byte[] key, CancellationToken ct)
    {
        var decryptRequest = new EncryptRequest
        {
            Plaintext = new MemoryStream(key),
            KeyId = options.Value.WrappingKeyArn,
        };
        var response = await kmsService.EncryptAsync(decryptRequest, ct);
        
        var result = response.CiphertextBlob.ToArray();
        await response.CiphertextBlob.DisposeAsync();
        
        return result;
    }
    
    private async Task<(byte[] encrypted, byte[] decrypted)> GenerateDataKeyAsync(EncryptionType encryptionType, CancellationToken ct)
    {
        var spec = encryptionType switch
        {
            EncryptionType.AES128 => DataKeySpec.AES_128,
            EncryptionType.AES256 => DataKeySpec.AES_256,
            _ => throw new ArgumentOutOfRangeException(nameof(encryptionType), encryptionType, null)
        };

        var request = new GenerateDataKeyRequest
        {
            KeyId = options.Value.WrappingKeyArn,
            KeySpec = spec
        };
        var result = await kmsService.GenerateDataKeyAsync(request, ct);
        var encrypted = result.CiphertextBlob.ToArray();
        var decrypted = result.Plaintext.ToArray();
        
        await result.Plaintext.DisposeAsync();
        await result.CiphertextBlob.DisposeAsync();

        return (encrypted, decrypted);
    }
}