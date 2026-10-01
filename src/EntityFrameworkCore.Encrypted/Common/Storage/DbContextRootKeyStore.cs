using System.Data.Common;
using System.Transactions;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
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
            // also with a retrying execution strategy, e.g. while the database is being created
            catch (Exception ex) when (ex is DbException or RetryLimitExceededException { InnerException: DbException })
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
                await InsertAsync(context, rootKey, cancellationToken);
                return true;
            });
        }
        catch (DbException)
        {
            // primary key violation: created concurrently by another instance; any other failure is rethrown
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

    // not SaveChanges: the application's SaveChanges interceptors and overrides would run for the key row, and may add
    // encrypted values (e.g. audit entries) that wait for the very key being stored
    private static async Task InsertAsync(DbContext context, WrappedRootKey rootKey, CancellationToken cancellationToken)
    {
        var entityType = context.Model.FindEntityType(typeof(EncryptionKeyEntity))!;
        var table = StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();

        (string Property, object Value)[] values =
        [
            (nameof(EncryptionKeyEntity.Id), rootKey.Id),
            (nameof(EncryptionKeyEntity.WrappingKeyId), rootKey.WrappingKeyId),
            (nameof(EncryptionKeyEntity.WrappedKey), rootKey.WrappedKey),
            (nameof(EncryptionKeyEntity.CreatedAt), rootKey.CreatedAt)
        ];

        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;

            var columns = new List<string>();
            var parameters = new List<string>();

            for (var i = 0; i < values.Length; i++)
            {
                var property = entityType.FindProperty(values[i].Property)!;
                var name = $"p{i}";

                columns.Add(sql.DelimitIdentifier(property.GetColumnName(table)!));
                parameters.Add(sql.GenerateParameterNamePlaceholder(name));
                command.Parameters.Add(property.GetRelationalTypeMapping()
                    .CreateParameter(command, sql.GenerateParameterName(name), values[i].Value, nullable: false));
            }

            command.CommandText = $"INSERT INTO {sql.DelimitIdentifier(table.Name, table.Schema)} " +
                                  $"({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)})";

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
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
