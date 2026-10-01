using System.Data.Common;
using EntityFrameworkCore.Encrypted.Common.Crypto;
using EntityFrameworkCore.Encrypted.Common.Diagnostics;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Common.Plugin;
using EntityFrameworkCore.Encrypted.Common.Storage;
using EntityFrameworkCore.Encrypted.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.Encrypted.Common.Maintenance;

/// <summary>
/// Scans encrypted columns with plain SQL built from the relational model: stored ciphertext is read as is,
/// without materializing entities, so values are inspected and re-encrypted without the change tracker,
/// interceptors or concurrency tokens being involved.
/// </summary>
internal sealed class EncryptedDataMaintenance(IServiceScopeFactory scopeFactory, DataKeyRing keyRing, ILogger<EncryptedDataMaintenance> logger)
{
    public async Task<IReadOnlyList<KeyUsage>> GetKeyUsageAsync(Type contextType, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("key_usage", contextType);
        var counts = new Dictionary<(string Table, string Column, KeyId? KeyId), long>();

        await using var scope = scopeFactory.CreateAsyncScope();
        await using var owned = OwnedContext.Create(scope.ServiceProvider, contextType);

        foreach (var table in EncryptedTable.From(owned.Context, logger))
        {
            await ScanAsync(owned.Context, table, (_, values) =>
            {
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    if (values[i] is not { } value)
                        continue;

                    var key = (table.DisplayName, table.Columns[i].Name, TryReadKeyId(table.Columns[i], value));
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }

                return ValueTask.CompletedTask;
            }, cancellationToken);
        }

        return counts
            .Select(x => new KeyUsage(x.Key.Table, x.Key.Column, x.Key.KeyId?.RootKeyId, x.Key.KeyId?.DataKeyVersion, x.Value))
            .OrderBy(x => x.Table).ThenBy(x => x.Column).ThenBy(x => x.RootKeyId).ThenBy(x => x.DataKeyVersion)
            .ToList();
    }

    public async Task<ReEncryptionResult> ReEncryptAsync(Type contextType, int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        using var activity = Telemetry.StartActivity("reencrypt", contextType)?.SetTag("batch_size", batchSize);

        // encrypt with the latest root key, also when it was rotated by another instance
        await keyRing.InitializeAsync(contextType, cancellationToken);
        await keyRing.RefreshAsync(contextType, cancellationToken);

        long reEncrypted = 0, skipped = 0, invalid = 0;

        await using var readerScope = scopeFactory.CreateAsyncScope();
        await using var reader = OwnedContext.Create(readerScope.ServiceProvider, contextType);

        // updates run on a second connection: the reader streams the table on the first one
        await using var writerScope = scopeFactory.CreateAsyncScope();
        await using var writer = OwnedContext.Create(writerScope.ServiceProvider, contextType);

        foreach (var table in EncryptedTable.From(reader.Context, logger))
        {
            if (table.KeyColumns.Count == 0)
            {
                logger.LogWarning("{Table} has no primary key, its encrypted values can't be re-encrypted", table.DisplayName);
                continue;
            }

            var pending = new List<Update>(batchSize);
            var active = keyRing.GetEncryptionKey(contextType).KeyId;

            async ValueTask FlushAsync()
            {
                var (updated, conflicts) = await ExecuteAsync(writer.Context, pending, cancellationToken);
                reEncrypted += updated;
                skipped += conflicts;
                Telemetry.RecordReEncryption(contextType, "reencrypted", updated);
                Telemetry.RecordReEncryption(contextType, "skipped", conflicts);
                pending.Clear();

                logger.LogInformation("Re-encrypted {Count} values so far, now in {Table}", reEncrypted, table.DisplayName);
                active = keyRing.GetEncryptionKey(contextType).KeyId;
            }

            await ScanAsync(reader.Context, table, async (keys, values) =>
            {
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    if (values[i] is not { } value)
                        continue;

                    var column = table.Columns[i];

                    if (TryReadKeyId(column, value) == active)
                        continue;

                    if (TryReEncrypt(table, column, value) is { } reEncryptedValue)
                        pending.Add(new Update(table, column.Name, column.Name, keys, value, reEncryptedValue));
                    else
                        invalid++;

                    if (pending.Count >= batchSize)
                        await FlushAsync();
                }
            }, cancellationToken);

            if (pending.Count > 0)
                await FlushAsync();
        }

        if (invalid > 0)
            logger.LogWarning("{Count} values of {Context} can't be decrypted and were left as is: not encrypted, or their root key is missing",
                invalid, contextType.Name);

        Telemetry.RecordReEncryption(contextType, "invalid", invalid);
        activity?.SetTag("reencrypted", reEncrypted).SetTag("skipped", skipped).SetTag("invalid", invalid);

        return new ReEncryptionResult(reEncrypted, skipped, invalid);
    }

    public async Task<long> RebuildBlindIndexesAsync(Type contextType, int batchSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        using var activity = Telemetry.StartActivity("blind_index.rebuild", contextType)?.SetTag("batch_size", batchSize);
        await keyRing.InitializeAsync(contextType, cancellationToken);
        await keyRing.LoadIndexKeysAsync(contextType, cancellationToken);

        long rebuilt = 0;

        await using var readerScope = scopeFactory.CreateAsyncScope();
        await using var reader = OwnedContext.Create(readerScope.ServiceProvider, contextType);

        await using var writerScope = scopeFactory.CreateAsyncScope();
        await using var writer = OwnedContext.Create(writerScope.ServiceProvider, contextType);

        foreach (var table in EncryptedTable.From(reader.Context, logger).Where(x => x.Columns.Any(c => c.Index != null)))
        {
            if (table.KeyColumns.Count == 0)
            {
                logger.LogWarning("{Table} has no primary key, its blind indexes can't be rebuilt", table.DisplayName);
                continue;
            }

            var pending = new List<Update>(batchSize);

            async ValueTask FlushAsync()
            {
                rebuilt += (await ExecuteAsync(writer.Context, pending, cancellationToken)).Updated;
                pending.Clear();
                logger.LogInformation("Rebuilt {Count} blind indexes so far, now in {Table}", rebuilt, table.DisplayName);
            }

            await ScanAsync(reader.Context, table, async (keys, values) =>
            {
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    var column = table.Columns[i];

                    if (column.Index is not { } index || values[i] is not { } value)
                        continue;

                    var hash = TryComputeIndex(table, column, value);

                    if (hash == null || values[table.Columns.Count + i] is byte[] stored && stored.AsSpan().SequenceEqual(hash))
                        continue;

                    pending.Add(new Update(table, index.Column, column.Name, keys, value, hash));

                    if (pending.Count >= batchSize)
                        await FlushAsync();
                }
            }, cancellationToken);

            if (pending.Count > 0)
                await FlushAsync();
        }

        activity?.SetTag("rebuilt", rebuilt);
        return rebuilt;
    }

    private byte[]? TryComputeIndex(EncryptedTable table, EncryptedColumn column, object value)
    {
        try
        {
            var plaintext = column.Converter.Encryptor.Decrypt(column.Converter.ToEnvelope(value));

            try
            {
                return column.Index!.Indexer.ComputeFromPlaintext(plaintext, ((ValueConverter)column.Converter).ProviderClrType == typeof(string));
            }
            finally
            {
                Array.Clear(plaintext);
            }
        }
        catch (Exception ex) when (ex is EntityFrameworkEncryptionException or FormatException or InvalidCastException)
        {
            logger.LogDebug(ex, "Value of {Table}.{Column} can't be decrypted, its blind index is left as is", table.DisplayName, column.Name);
            return null;
        }
    }

    private object? TryReEncrypt(EncryptedTable table, EncryptedColumn column, object value)
    {
        try
        {
            var encryptor = column.Converter.Encryptor;
            var plaintext = encryptor.Decrypt(column.Converter.ToEnvelope(value));

            try
            {
                return column.Converter.FromEnvelope(encryptor.Encrypt(plaintext));
            }
            finally
            {
                Array.Clear(plaintext);
            }
        }
        catch (Exception ex) when (ex is EntityFrameworkEncryptionException or FormatException or InvalidCastException)
        {
            logger.LogDebug(ex, "Value of {Table}.{Column} can't be decrypted and is left as is", table.DisplayName, column.Name);
            return null;
        }
    }

    private static KeyId? TryReadKeyId(EncryptedColumn column, object value)
    {
        try
        {
            return Envelope.ReadKeyId(column.Converter.ToEnvelope(value));
        }
        catch (Exception ex) when (ex is EntityFrameworkEncryptionException or FormatException or InvalidCastException)
        {
            return null;
        }
    }

    private static async Task ScanAsync(
        DbContext context, EncryptedTable table, Func<object[], object?[], ValueTask> onRow, CancellationToken cancellationToken)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        // keys, encrypted values, then blind indexes (NULL for columns without one)
        var columns = table.KeyColumns.Select(x => sql.DelimitIdentifier(x))
            .Concat(table.Columns.Select(x => sql.DelimitIdentifier(x.Name)))
            .Concat(table.Columns.Select(x => x.Index is { } index ? sql.DelimitIdentifier(index.Column) : "NULL"));
        var notNull = table.Columns.Select(x => $"{sql.DelimitIdentifier(x.Name)} IS NOT NULL");

        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT {string.Join(", ", columns)} FROM {sql.DelimitIdentifier(table.Name, table.Schema)} " +
                                  $"WHERE {string.Join(" OR ", notNull)}";
            command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var keys = new object[table.KeyColumns.Count];
                var values = new object?[table.Columns.Count * 2];

                for (var i = 0; i < keys.Length; i++)
                    keys[i] = reader.GetValue(i);

                for (var i = 0; i < values.Length; i++)
                    values[i] = reader.IsDBNull(keys.Length + i) ? null : reader.GetValue(keys.Length + i);

                await onRow(keys, values);
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <returns>Number of updated values, and of values changed concurrently (left as is).</returns>
    private static Task<(long Updated, long Conflicts)> ExecuteAsync(
        DbContext context, IReadOnlyList<Update> updates, CancellationToken cancellationToken)
        // retrying strategies (EnableRetryOnFailure) only allow transactions they run; retrying a batch is safe:
        // values already updated no longer match their previous value
        => context.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            var sql = context.GetService<ISqlGenerationHelper>();
            long updated = 0;

            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var connection = context.Database.GetDbConnection();

            if (connection.CanCreateBatch)
            {
                await using var batch = connection.CreateBatch();
                batch.Transaction = transaction.GetDbTransaction();

                foreach (var update in updates)
                {
                    var command = batch.CreateBatchCommand();
                    command.CommandText = update.ToSql(sql);
                    update.AddParameters(command.Parameters, command.CreateParameter, sql);
                    batch.BatchCommands.Add(command);
                }

                await batch.ExecuteNonQueryAsync(ct);
                updated = batch.BatchCommands.Sum(x => (long)x.RecordsAffected);
            }
            else
            {
                foreach (var update in updates)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction.GetDbTransaction();
                    command.CommandText = update.ToSql(sql);
                    update.AddParameters(command.Parameters, command.CreateParameter, sql);

                    updated += await command.ExecuteNonQueryAsync(ct);
                }
            }

            await transaction.CommitAsync(ct);
            return (updated, updates.Count - updated);
        }, cancellationToken);

    private sealed record EncryptedColumn(string Name, IEncryptionConverter Converter, BlindIndexColumn? Index);

    private sealed record BlindIndexColumn(string Column, BlindIndexer Indexer);

    private sealed record EncryptedTable(string Name, string? Schema, IReadOnlyList<string> KeyColumns, IReadOnlyList<EncryptedColumn> Columns)
    {
        public string DisplayName => Schema == null ? Name : $"{Schema}.{Name}";

        private static BlindIndexColumn? FindBlindIndex(ITable table, IColumn column)
        {
            var store = StoreObjectIdentifier.Table(table.Name, table.Schema);

            return column.PropertyMappings
                .Select(x => BlindIndex.Find(x.Property))
                .FirstOrDefault(x => x != null) is { } index
                ? new BlindIndexColumn(index.GetColumnName(store)!, ((IBlindIndexConverter)index.GetValueConverter()!).Indexer)
                : null;
        }

        public static IEnumerable<EncryptedTable> From(DbContext context, ILogger logger)
        {
            var model = context.Model;

            foreach (var entityType in model.GetEntityTypes().Where(x => x.IsMappedToJson()))
            {
                if (entityType.GetDeclaredProperties().Any(x => x.GetValueConverter() is IEncryptionConverter))
                    logger.LogWarning("{Entity} is mapped to JSON: its encrypted values are not scanned", entityType.DisplayName());
            }

            foreach (var table in model.GetRelationalModel().Tables)
            {
                var columns = new List<EncryptedColumn>();

                foreach (var column in table.Columns)
                {
                    var converters = column.PropertyMappings
                        .Select(x => x.Property.GetValueConverter())
                        .OfType<IEncryptionConverter>()
                        .DistinctBy(x => x.Encryptor.Label)
                        .ToList();

                    if (converters.Count > 1)
                        throw new EntityFrameworkEncryptionException(
                            $"Column {table.Name}.{column.Name} is mapped to encrypted properties with different labels");

                    if (converters.Count == 1)
                        columns.Add(new EncryptedColumn(column.Name, converters[0], FindBlindIndex(table, column)));
                }

                if (columns.Count > 0)
                    yield return new EncryptedTable(
                        table.Name,
                        table.Schema,
                        table.PrimaryKey?.Columns.Select(x => x.Name).ToList() ?? [],
                        columns);
            }
        }
    }

    /// <summary>
    /// Sets a column only if the encrypted value is still the one that was read: values written concurrently by the
    /// application are already encrypted with the active key, and their blind index is set as well, so they are left as is.
    /// </summary>
    /// <param name="Column">Column to set: the encrypted value itself, or its blind index.</param>
    /// <param name="CheckedColumn">Encrypted column that must still contain <paramref name="OldValue"/>.</param>
    private sealed record Update(EncryptedTable Table, string Column, string CheckedColumn, object[] Keys, object OldValue, object NewValue)
    {
        public string ToSql(ISqlGenerationHelper sql)
        {
            var keys = Table.KeyColumns.Select((x, i) => $"{sql.DelimitIdentifier(x)} = {sql.GenerateParameterNamePlaceholder($"k{i}")}");

            return $"UPDATE {sql.DelimitIdentifier(Table.Name, Table.Schema)} " +
                   $"SET {sql.DelimitIdentifier(Column)} = {sql.GenerateParameterNamePlaceholder("new_value")} " +
                   $"WHERE {string.Join(" AND ", keys)} AND {sql.DelimitIdentifier(CheckedColumn)} = {sql.GenerateParameterNamePlaceholder("old_value")}";
        }

        public void AddParameters(DbParameterCollection parameters, Func<DbParameter> create, ISqlGenerationHelper sql)
        {
            Add("new_value", NewValue);
            Add("old_value", OldValue);

            for (var i = 0; i < Keys.Length; i++)
                Add($"k{i}", Keys[i]);

            void Add(string name, object value)
            {
                var parameter = create();
                parameter.ParameterName = sql.GenerateParameterName(name);
                parameter.Value = value;
                parameters.Add(parameter);
            }
        }
    }
}
