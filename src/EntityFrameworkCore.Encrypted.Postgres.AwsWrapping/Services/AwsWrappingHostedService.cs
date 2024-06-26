using System.Data.Common;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime.Internal;
using EntityFrameworkCore.Encrypted.Common;
using EntityFrameworkCore.Encrypted.Common.Abstractions;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Services;

internal class AwsKeyWrappingHostedService(
    IServiceScopeFactory scopeProvider,
    IKeyStorage keyStorage,
    IAmazonKeyManagementService kmsService,
    IDbContextFactory<EncryptionMetadataContext> dbContextFactory,
    AwsWrappingOptions wrappingOptions,
    ILogger<AwsKeyWrappingHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeProvider.CreateAsyncScope();

        foreach (var info in scope.ServiceProvider.GetEncryptionInfo())
        {
            do
            {
                try
                {
                    await InitializeDataKeyAsync(info.ContextName, info.EncryptionType, cancellationToken);

                    logger.LogInformation("Data encryption key for {Context} initialized successfully", info.ContextName);
                    
                    break;
                }
                catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    // simultaneous data key creation may rarely occur when multiple instances of the same service
                    // trying to created data key for the very first time if it is not exists in the database
                    // should be ignored and retried
                    logger.LogWarning("Concurrent key creation for {Context}. Retrying ...", info.ContextName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Data encryption key for {Context} initialization failed", info.ContextName);
                    throw;
                }
            } while (!cancellationToken.IsCancellationRequested);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private async Task InitializeDataKeyAsync(string contextName, EncryptionType encryptionType, CancellationToken ct)
    {
        if (keyStorage.ContainsKey(contextName))
            return;
        
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var metadata = await context.Metadata
            .FirstOrDefaultAsync(x => x.ContextId == contextName, ct);

        if (metadata == null && !wrappingOptions.GenerateDataKeyIfNotExist)
        {
            throw new EntityFrameworkEncryptionException(
                $"Data encryption key for {contextName} not found");
        }

        byte[] dataKey;

        if (metadata != null)
        {
            dataKey = await DecryptAsync(metadata.Key, ct);

            if (wrappingOptions.ReEncryptDataKeyOnStart)
            {
                var reEncryptedDataKey = await EncryptAsync(dataKey, ct);

                await VerifyAsync(dataKey, reEncryptedDataKey, ct);

                metadata.Update(reEncryptedDataKey);
                await context.SaveChangesAsync(ct);
            }
        }
        else
        {
            var (encrypted, decrypted) =
                await GenerateDataKeyAsync(encryptionType, ct);

            dataKey = decrypted;

            context.Metadata.Add(EncryptionMetadata.Create(contextName, encrypted));

            await context.SaveChangesAsync(ct);
        }

        keyStorage.AddKey(contextName, dataKey);
    }
    
    private async Task<byte[]> DecryptAsync(byte[] key, CancellationToken ct)
    {
        var decryptRequest = new DecryptRequest
        {
            CiphertextBlob = new MemoryStream(key),
            KeyId = wrappingOptions.WrappingKeyArn,
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
            KeyId = wrappingOptions.WrappingKeyArn,
        };
        var response = await kmsService.EncryptAsync(decryptRequest, ct);
        
        var result = response.CiphertextBlob.ToArray();
        await response.CiphertextBlob.DisposeAsync();
        
        return result;
    }
    
    private async Task VerifyAsync(byte[] decryptedKey, byte[] reEncryptedDataKey, CancellationToken ct)
    {
        var verificationDecryptedKey = await DecryptAsync(reEncryptedDataKey, ct);
        var verified = decryptedKey.SequenceEqual(verificationDecryptedKey);

        if (!verified)
            throw new EntityFrameworkEncryptionException("Original data key not the same as re-encrypted");
    }
    
    private async Task<(byte[] encrypted, byte[] decrypted)> GenerateDataKeyAsync(EncryptionType encryptionType, CancellationToken ct)
    {
        var spec = encryptionType switch
        {
            EncryptionType.Aes128 => DataKeySpec.AES_128,
            EncryptionType.Aes256 => DataKeySpec.AES_256,
            _ => throw new ArgumentOutOfRangeException(nameof(encryptionType), encryptionType, "Data key type is not supported by AWS")
        };

        var request = new GenerateDataKeyRequest
        {
            KeyId = wrappingOptions.WrappingKeyArn,
            KeySpec = spec,
            EncryptionContext = new AutoConstructedDictionary<string, string>()
        };
        
        var result = await kmsService.GenerateDataKeyAsync(request, ct);
        var encrypted = result.CiphertextBlob.ToArray();
        var decrypted = result.Plaintext.ToArray();
        
        await result.Plaintext.DisposeAsync();
        await result.CiphertextBlob.DisposeAsync();

        return (encrypted, decrypted);
    }
}