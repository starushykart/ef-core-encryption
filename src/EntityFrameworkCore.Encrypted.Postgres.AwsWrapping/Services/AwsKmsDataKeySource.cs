using System.Data.Common;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using EntityFrameworkCore.Encrypted.Common.Abstractions;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Services;

/// <summary>
/// Envelope encryption: the data key is stored in the database wrapped (encrypted) by an AWS KMS key.
/// </summary>
internal sealed class AwsKmsDataKeySource(
    IAmazonKeyManagementService kmsService,
    IDbContextFactory<EncryptionMetadataContext> dbContextFactory,
    AwsWrappingOptions wrappingOptions,
    ILogger<AwsKmsDataKeySource> logger) : IDataKeySource
{
    public async ValueTask<byte[]> GetDataKeyAsync(DataKeyContext context, CancellationToken cancellationToken)
    {
        // stored id is the context type name (not full name) to stay compatible with existing metadata rows
        var contextId = context.DbContextType.Name;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await GetOrCreateDataKeyAsync(contextId, cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is DbException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // simultaneous data key creation may rarely occur when multiple instances of the same service
                // try to create the data key for the very first time; the next attempt reads the stored key
                logger.LogWarning("Concurrent key creation for {Context}. Retrying ...", contextId);
            }
        }
    }

    private async Task<byte[]> GetOrCreateDataKeyAsync(string contextId, CancellationToken ct)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var metadata = await context.Metadata
            .FirstOrDefaultAsync(x => x.ContextId == contextId, ct);

        if (metadata == null && !wrappingOptions.GenerateDataKeyIfNotExist)
        {
            throw new EntityFrameworkEncryptionException(
                $"Data encryption key for {contextId} not found");
        }

        if (metadata == null)
        {
            var (encrypted, decrypted) = await GenerateDataKeyAsync(ct);

            context.Metadata.Add(EncryptionMetadata.Create(contextId, encrypted));
            await context.SaveChangesAsync(ct);

            return decrypted;
        }

        var dataKey = await DecryptAsync(metadata.Key, ct);

        if (wrappingOptions.ReEncryptDataKeyOnStart)
        {
            var reEncryptedDataKey = await EncryptAsync(dataKey, ct);

            await VerifyAsync(dataKey, reEncryptedDataKey, ct);

            metadata.Update(reEncryptedDataKey);
            await context.SaveChangesAsync(ct);
        }

        return dataKey;
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
        var encryptRequest = new EncryptRequest
        {
            Plaintext = new MemoryStream(key),
            KeyId = wrappingOptions.WrappingKeyArn,
        };
        var response = await kmsService.EncryptAsync(encryptRequest, ct);

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

    private async Task<(byte[] encrypted, byte[] decrypted)> GenerateDataKeyAsync(CancellationToken ct)
    {
        var request = new GenerateDataKeyRequest
        {
            KeyId = wrappingOptions.WrappingKeyArn,
            KeySpec = DataKeySpec.AES_256
        };

        var result = await kmsService.GenerateDataKeyAsync(request, ct);
        var encrypted = result.CiphertextBlob.ToArray();
        var decrypted = result.Plaintext.ToArray();

        await result.Plaintext.DisposeAsync();
        await result.CiphertextBlob.DisposeAsync();

        return (encrypted, decrypted);
    }
}
