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
using Microsoft.Extensions.Options;
using Npgsql;

namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Internal;

internal class AwsKeyWrappingHostedService(
    IServiceScopeFactory scopeProvider,
    IKeyStorage keyStorage,
    IAmazonKeyManagementService kmsService,
    IDbContextFactory<EncryptionMetadataContext> dbContextFactory,
    IOptions<WrappingOptions> options,
    ILogger<AwsKeyWrappingHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var counter = 0;
        
        while (!cancellationToken.IsCancellationRequested && counter < options.Value.MaxInitializationRetryCount)
        {
            counter += 1;

            try
            {
                await using var scope = scopeProvider.CreateAsyncScope();

                var registeredDbContexts = scope
                    .ServiceProvider.GetServices<DbContextOptions>()
                    .Where(x => x.FindExtension<EncryptionDbContextOptionsExtension>() != null)
                    .Select(x => new
                    {
                        ContextName = x.ContextType.Name,
                        EncryptionType = x.GetExtension<EncryptionDbContextOptionsExtension>().GetEncryptionType()
                    });

                foreach (var data in registeredDbContexts)
                {
                    await InitializeDataKeyAsync(data.ContextName, data.EncryptionType, cancellationToken);
                    
                    logger.LogInformation("Data encryption key for {ContextType} initialized successfully", data.ContextName);
                }

                return;
            }
            catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // simultaneous creation may rarely occur when multiple instances of the same service
                // trying to created data key for the very first time if it is not exists
                // should be ignored and retried
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        throw new EntityFrameworkEncryptionException("Encryption key storage cannot be initialized");
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

        if (metadata == null && !options.Value.GenerateDataKeyIfNotExist)
        {
            throw new EntityFrameworkEncryptionException(
                $"Data encryption key for {contextName} not found");
        }

        byte[] dataKey;

        if (metadata != null)
        {
            dataKey = await DecryptAsync(metadata.Key, ct);

            if (options.Value.ReEncryptDataKeyOnStart)
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
            EncryptionType.Aes128 => DataKeySpec.AES_128,
            EncryptionType.Aes256 => DataKeySpec.AES_256,
            _ => throw new ArgumentOutOfRangeException(nameof(encryptionType), encryptionType, "Data key type is not supported by AWS")
        };

        var request = new GenerateDataKeyRequest
        {
            KeyId = options.Value.WrappingKeyArn,
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