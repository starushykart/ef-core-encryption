using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Keys;

namespace EntityFrameworkCore.Encrypted.Common.Keys;

/// <summary>Root keys configured in code or configuration (<c>UseKey</c>); shared by all contexts.</summary>
internal sealed class StaticRootKeyProvider(IReadOnlyDictionary<int, byte[]> keys) : IRootKeyProvider
{
    public Task<RootKey> GetActiveRootKeyAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        var id = keys.Keys.Max();
        return Task.FromResult(new RootKey(id, keys[id]));
    }

    public Task<RootKey?> GetRootKeyAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken)
        => Task.FromResult(keys.TryGetValue(rootKeyId, out var key) ? new RootKey(rootKeyId, key) : null);

    public Task<int?> GetActiveRootKeyIdAsync(Type dbContextType, CancellationToken cancellationToken)
        => Task.FromResult<int?>(keys.Keys.Max());

    public Task<RootKey> RotateRootKeyAsync(Type dbContextType, CancellationToken cancellationToken)
        => throw new EntityFrameworkEncryptionException(
            "Static root keys can't be rotated at runtime. Add a key with a higher id to the configuration instead");

    public Task<int> RewrapRootKeysAsync(Type dbContextType, CancellationToken cancellationToken)
        => throw new EntityFrameworkEncryptionException(
            "Static root keys are not wrapped by a key management service, so there is nothing to rewrap");
}
