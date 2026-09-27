using System.Data.Common;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encrypted.Common.Storage;

/// <summary>
/// Stores wrapped root keys in the <c>__EncryptionKeys</c> table of the encrypted context itself,
/// so keys live (and are backed up and replicated) together with the data they protect.
/// </summary>
internal sealed class DbContextRootKeyStore(IServiceScopeFactory scopeFactory) : IRootKeyStore
{
    public async Task<IReadOnlyList<WrappedRootKey>> GetAllAsync(Type dbContextType, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var owned = CreateContext(scope.ServiceProvider, dbContextType);

        try
        {
            return await owned.Context.Set<EncryptionKeyEntity>()
                .AsNoTracking()
                .OrderBy(x => x.Id)
                .Select(x => new WrappedRootKey(x.Id, x.WrappingKeyId, x.WrappedKey, x.CreatedAt))
                .ToListAsync(cancellationToken);
        }
        catch (DbException ex)
        {
            throw new EncryptionKeyStoreUnavailableException(
                $"Can't read {EncryptionKeyEntity.TableName} of {dbContextType.Name}. Make sure the database is migrated", ex);
        }
    }

    public async Task<bool> TryAddAsync(Type dbContextType, WrappedRootKey rootKey, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var owned = CreateContext(scope.ServiceProvider, dbContextType);

        owned.Context.Add(new EncryptionKeyEntity
        {
            Id = rootKey.Id,
            WrappingKeyId = rootKey.WrappingKeyId,
            WrappedKey = rootKey.WrappedKey,
            CreatedAt = rootKey.CreatedAt
        });

        try
        {
            await owned.Context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // primary key violation: created concurrently by another instance; any other failure is rethrown
            if (await ExistsAsync(dbContextType, rootKey.Id, cancellationToken))
                return false;

            throw;
        }
    }

    private async Task<bool> ExistsAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken)
    {
        // separate context: the failed one still tracks the rejected row
        await using var scope = scopeFactory.CreateAsyncScope();
        await using var owned = CreateContext(scope.ServiceProvider, dbContextType);

        return await owned.Context.Set<EncryptionKeyEntity>().AnyAsync(x => x.Id == rootKeyId, cancellationToken);
    }

    private static OwnedContext CreateContext(IServiceProvider serviceProvider, Type dbContextType)
    {
        // AddDbContext / AddDbContextPool / AddDbContextFactory register the context as a scoped service
        if (serviceProvider.GetService(dbContextType) is DbContext scoped)
            return new OwnedContext(scoped, dispose: false);

        // AddPooledDbContextFactory registers only the factory
        var factoryType = typeof(IDbContextFactory<>).MakeGenericType(dbContextType);
        var factory = serviceProvider.GetService(factoryType)
            ?? throw new EntityFrameworkEncryptionException(
                $"{dbContextType.Name} is not registered in dependency injection, so its keys can't be loaded");

        var context = (DbContext)factoryType.GetMethod(nameof(IDbContextFactory<DbContext>.CreateDbContext))!.Invoke(factory, null)!;
        return new OwnedContext(context, dispose: true);
    }

    private readonly struct OwnedContext(DbContext context, bool dispose) : IAsyncDisposable
    {
        public DbContext Context { get; } = context;

        public ValueTask DisposeAsync()
            => dispose ? Context.DisposeAsync() : ValueTask.CompletedTask;
    }
}
