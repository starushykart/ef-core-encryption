using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Keys;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.Encrypted.Common.Keys;

/// <summary>
/// Root keys configured in code or configuration (<c>UseKey</c>); shared by all contexts.
/// The blind index key is random per context and stored in the key store wrapped with the active static key,
/// so rotating static keys doesn't change it: it is rewrapped with the new key when it's loaded.
/// </summary>
internal sealed class StaticRootKeyProvider(
    IReadOnlyDictionary<int, byte[]> keys,
    IRootKeyStore store,
    TimeProvider timeProvider,
    EncryptionSettings settings,
    ILogger<StaticRootKeyProvider> logger) : IRootKeyProvider
{
    private const string WrappingKeyPrefix = "static:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("efenc:index-key");

    private int ActiveId => keys.Keys.Max();

    public Task<RootKey> GetActiveRootKeyAsync(Type dbContextType, CancellationToken cancellationToken)
        => Task.FromResult(new RootKey(ActiveId, keys[ActiveId]));

    public Task<RootKey?> GetRootKeyAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken)
        => Task.FromResult(keys.TryGetValue(rootKeyId, out var key) ? new RootKey(rootKeyId, key) : null);

    public Task<int?> GetActiveRootKeyIdAsync(Type dbContextType, CancellationToken cancellationToken)
        => Task.FromResult<int?>(ActiveId);

    public Task<RootKey> RotateRootKeyAsync(Type dbContextType, CancellationToken cancellationToken)
        => throw new EntityFrameworkEncryptionException(
            "Static root keys can't be rotated at runtime. Add a key with a higher id to the configuration instead");

    public Task<int> RewrapRootKeysAsync(Type dbContextType, CancellationToken cancellationToken)
        => throw new EntityFrameworkEncryptionException(
            "Static root keys are not wrapped by a key management service, so there is nothing to rewrap");

    public async Task<byte[]> GetIndexKeyAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        var stored = (await store.GetAllAsync(dbContextType, cancellationToken)).FirstOrDefault(x => x.Id == IRootKeyProvider.IndexKeyId);

        if (stored == null)
            return await CreateIndexKeyAsync(dbContextType, cancellationToken);

        var wrappingKeyId = ParseWrappingKeyId(dbContextType, stored);
        var indexKey = Unwrap(dbContextType, stored, wrappingKeyId);

        // wrapped with an older static key: rewrap with the active one, so the old key can be removed later
        // best effort: the key is usable either way, e.g. when the application may only read the key store
        if (wrappingKeyId != ActiveId)
        {
            try
            {
                await store.UpdateAsync(dbContextType, Wrap(indexKey, stored.CreatedAt), cancellationToken);
                logger.LogInformation("Blind index key of {Context} rewrapped with static key {KeyId}", dbContextType.Name, ActiveId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "Blind index key of {Context} can't be rewrapped with static key {KeyId}: keep static key {PreviousKeyId} until it is",
                    dbContextType.Name, ActiveId, wrappingKeyId);
            }
        }

        return indexKey;
    }

    private async Task<byte[]> CreateIndexKeyAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        if (!settings.CreateRootKeyIfMissing)
            throw new EntityFrameworkEncryptionException(
                $"Blind index key of {dbContextType.Name} not found and creating it is disabled");

        var indexKey = RandomNumberGenerator.GetBytes(32);

        if (await store.TryAddAsync(dbContextType, Wrap(indexKey, timeProvider.GetUtcNow()), cancellationToken))
            return indexKey;

        // created concurrently by another instance: use theirs
        CryptographicOperations.ZeroMemory(indexKey);
        return await GetIndexKeyAsync(dbContextType, cancellationToken);
    }

    private WrappedRootKey Wrap(byte[] indexKey, DateTimeOffset createdAt)
    {
        var wrapped = new byte[NonceSize + indexKey.Length + TagSize];
        var nonce = wrapped.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(keys[ActiveId], TagSize);
        aes.Encrypt(nonce, indexKey, wrapped.AsSpan(NonceSize, indexKey.Length), wrapped.AsSpan(NonceSize + indexKey.Length), AssociatedData);

        return new WrappedRootKey(IRootKeyProvider.IndexKeyId, WrappingKeyPrefix + ActiveId.ToString(CultureInfo.InvariantCulture), wrapped, createdAt);
    }

    private byte[] Unwrap(Type dbContextType, WrappedRootKey stored, int wrappingKeyId)
    {
        if (!keys.TryGetValue(wrappingKeyId, out var key))
            throw new EntityFrameworkEncryptionException(
                $"Blind index key of {dbContextType.Name} is wrapped with static key {wrappingKeyId}, which is no longer configured. " +
                $"Add it back: the blind index key is rewrapped with the active key on the next start, then it can be removed");

        if (stored.WrappedKey.Length <= NonceSize + TagSize)
            throw new EntityFrameworkEncryptionException($"Blind index key of {dbContextType.Name} is corrupted");

        var indexKey = new byte[stored.WrappedKey.Length - NonceSize - TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                stored.WrappedKey.AsSpan(0, NonceSize),
                stored.WrappedKey.AsSpan(NonceSize, indexKey.Length),
                stored.WrappedKey.AsSpan(NonceSize + indexKey.Length),
                indexKey,
                AssociatedData);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new EntityFrameworkEncryptionException(
                $"Blind index key of {dbContextType.Name} can't be unwrapped with static key {wrappingKeyId}: the key was changed", ex);
        }

        return indexKey;
    }

    private static int ParseWrappingKeyId(Type dbContextType, WrappedRootKey stored)
        => stored.WrappingKeyId.StartsWith(WrappingKeyPrefix, StringComparison.Ordinal)
           && int.TryParse(stored.WrappingKeyId.AsSpan(WrappingKeyPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new EntityFrameworkEncryptionException(
                $"Blind index key of {dbContextType.Name} is wrapped by '{stored.WrappingKeyId}', not by a static key. " +
                "Configure the key source that created it");
}
