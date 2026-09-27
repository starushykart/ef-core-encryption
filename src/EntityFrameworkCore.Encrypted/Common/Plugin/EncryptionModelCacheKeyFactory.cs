using System.Collections.Concurrent;
using EntityFrameworkCore.Encrypted.Common.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

/// <summary>
/// Value converters of the model capture the application key ring. EF shares one internal service provider
/// (and model cache) between applications with equal options, so the key ring is made part of the model cache key:
/// each application gets its own model without EF creating an internal service provider per application.
/// </summary>
internal sealed class EncryptionModelCacheKeyFactory(IModelCacheKeyFactory inner, IMemoryCache memoryCache) : IModelCacheKeyFactory
{
    private readonly ConcurrentDictionary<object, bool> _trackedKeys = new();

    public object Create(DbContext context, bool designTime)
    {
        var keyRing = context
            .GetService<IDbContextOptions>()
            .FindExtension<EncryptionDbContextOptionsExtension>()?
            .KeyRing;

        var key = new EncryptionModelCacheKey(inner.Create(context, designTime), keyRing);

        // the shared model cache is size limited (~40 models): evict models of disposed applications,
        // e.g. integration tests creating an application per test
        if (keyRing != null && _trackedKeys.TryAdd(key, true))
        {
            keyRing.OnDispose(() =>
            {
                memoryCache.Remove(key);
                _trackedKeys.TryRemove(key, out _);
            });
        }

        return key;
    }

    // records compare the key ring by reference: DataKeyRing does not override equality
    private sealed record EncryptionModelCacheKey(object InnerKey, DataKeyRing? KeyRing);
}
