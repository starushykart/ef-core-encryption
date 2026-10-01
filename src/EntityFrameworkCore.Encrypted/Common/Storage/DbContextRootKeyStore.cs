using System.Data.Common;
using System.Transactions;
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
    public Task<IReadOnlyList<WrappedRootKey>> GetAllAsync(Type dbContextType, CancellationToken cancellationToken)
        => InContextAsync<IReadOnlyList<WrappedRootKey>>(dbContextType, async context =>
        {
            try
            {
                return await context.Set<EncryptionKeyEntity>()
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
        });

    public async Task<bool> TryAddAsync(Type dbContextType, WrappedRootKey rootKey, CancellationToken cancellationToken)
    {
        try
        {
            return await InContextAsync(dbContextType, async context =>
            {
                context.Add(new EncryptionKeyEntity
                {
                    Id = rootKey.Id,
                    WrappingKeyId = rootKey.WrappingKeyId,
                    WrappedKey = rootKey.WrappedKey,
                    CreatedAt = rootKey.CreatedAt
                });

                await context.SaveChangesAsync(cancellationToken);
                return true;
            });
        }
        catch (DbUpdateException)
        {
            // primary key violation: created concurrently by another instance; any other failure is rethrown.
            // checked with a new context: the failed one still tracks the rejected row
            var exists = await InContextAsync(dbContextType, context =>
                context.Set<EncryptionKeyEntity>().AnyAsync(x => x.Id == rootKey.Id, cancellationToken));

            if (exists)
                return false;

            throw;
        }
    }

    public async Task UpdateAsync(Type dbContextType, WrappedRootKey rootKey, CancellationToken cancellationToken)
    {
        var updated = await InContextAsync(dbContextType, context => context.Set<EncryptionKeyEntity>()
            .Where(x => x.Id == rootKey.Id)
            .ExecuteUpdateAsync(x => x
                .SetProperty(e => e.WrappingKeyId, rootKey.WrappingKeyId)
                .SetProperty(e => e.WrappedKey, rootKey.WrappedKey), cancellationToken));

        if (updated == 0)
            throw new EntityFrameworkEncryptionException($"Root key {rootKey.Id} of {dbContextType.Name} not found");
    }

    private async Task<T> InContextAsync<T>(Type dbContextType, Func<DbContext, Task<T>> action)
    {
        // keys may be loaded lazily inside the application's TransactionScope: a rollback would remove a created key
        // that is already in use, and a second connection would need a distributed transaction
        using var suppress = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);

        await using var scope = scopeFactory.CreateAsyncScope();
        await using var owned = OwnedContext.Create(scope.ServiceProvider, dbContextType);

        return await action(owned.Context);
    }
}
