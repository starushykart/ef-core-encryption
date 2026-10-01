using System.Data.Common;
using System.Text;
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
    private const int PageSize = 1000;

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

        await using var scope = scopeFactory.CreateAsyncScope();
        await using var owned = OwnedContext.Create(scope.ServiceProvider, contextType);
        var context = owned.Context;

        foreach (var table in EncryptedTable.From(context, logger))
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
                var (updated, conflicts) = await ExecuteAsync(context, pending, cancellationToken);
                reEncrypted += updated;
                skipped += conflicts;
                Telemetry.RecordReEncryption(contextType, "reencrypted", updated);
                Telemetry.RecordReEncryption(contextType, "skipped", conflicts);
                pending.Clear();

                logger.LogInformation("Re-encrypted {Count} values so far, now in {Table}", reEncrypted, table.DisplayName);
                active = keyRing.GetEncryptionKey(contextType).KeyId;
            }

            await ScanAsync(context, table, async (keys, values) =>
            {
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    if (values[i] is not { } value)
                        continue;

                    var column = table.Columns[i];

                    if (TryReadKeyId(column, value) == active)
                        continue;

                    if (TryReEncrypt(table, column, value) is var (reEncryptedValue, index))
                    {
                        // reEncryptedValue is never null here: null results don't match the pattern
                        pending.Add(new Update(table, column.Store, column.Store, keys, value, reEncryptedValue));

                        // runs after the value update in the same transaction: matches only if that one did
                        if (index != null)
                            pending.Add(new Update(table, column.Index!.Store, column.Store, keys, reEncryptedValue, index, Counted: false));
                    }
                    else
                    {
                        invalid++;
                    }

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

        await using var scope = scopeFactory.CreateAsyncScope();
        await using var owned = OwnedContext.Create(scope.ServiceProvider, contextType);
        var context = owned.Context;

        foreach (var table in EncryptedTable.From(context, logger).Where(x => x.Columns.Any(c => c.Index != null)))
        {
            if (table.KeyColumns.Count == 0)
            {
                logger.LogWarning("{Table} has no primary key, its blind indexes can't be rebuilt", table.DisplayName);
                continue;
            }

            var pending = new List<Update>(batchSize);

            async ValueTask FlushAsync()
            {
                rebuilt += (await ExecuteAsync(context, pending, cancellationToken)).Updated;
                pending.Clear();
                logger.LogInformation("Rebuilt {Count} blind indexes so far, now in {Table}", rebuilt, table.DisplayName);
            }

            await ScanAsync(context, table, async (keys, values) =>
            {
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    var column = table.Columns[i];

                    if (column.Index is not { } index || values[i] is not { } value)
                        continue;

                    var hash = TryComputeIndex(table, column, value);

                    if (hash == null || values[table.Columns.Count + i] is byte[] stored && stored.AsSpan().SequenceEqual(hash))
                        continue;

                    pending.Add(new Update(table, index.Store, column.Store, keys, value, hash));

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
            var plaintext = DecryptStored(column, value, out _);

            try
            {
                return ComputeIndex(column, plaintext);
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

    /// <returns>
    /// The value encrypted with the active key, and for a legacy value with a blind index its index: legacy values
    /// can't be found by it until they are migrated.
    /// </returns>
    private (object Value, byte[]? Index)? TryReEncrypt(EncryptedTable table, EncryptedColumn column, object value)
    {
        try
        {
            var plaintext = DecryptStored(column, value, out var legacy);

            try
            {
                var reEncrypted = column.Converter.FromEnvelope(column.Converter.Encryptor.Encrypt(plaintext));
                return (reEncrypted, legacy && column.Index != null ? ComputeIndex(column, plaintext) : null);
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

    /// <summary>Plaintext of a stored value (UTF-8 for text): in the library's format, or read with the legacy decryptor.</summary>
    private static byte[] DecryptStored(EncryptedColumn column, object value, out bool legacy)
    {
        var encryptor = column.Converter.Encryptor;
        legacy = true;

        switch (value)
        {
            case string text when encryptor.TryDecryptLegacy(text, out var legacyText):
                return Encoding.UTF8.GetBytes(legacyText!);

            // copy: the decryptor may return the stored array, which is still needed and the plaintext is cleared
            case byte[] binary when encryptor.TryDecryptLegacy(binary, out var legacyBinary):
                return (byte[])legacyBinary!.Clone();
        }

        legacy = false;
        return encryptor.Decrypt(column.Converter.ToEnvelope(value));
    }

    private static byte[] ComputeIndex(EncryptedColumn column, byte[] plaintext)
        => column.Index!.Indexer.ComputeFromPlaintext(plaintext, ((ValueConverter)column.Converter).ProviderClrType == typeof(string));

    private static KeyId? TryReadKeyId(EncryptedColumn column, object value)
    {
        try
        {
            return column.Converter.Encryptor.TryReadKeyId(column.Converter.ToEnvelope(value));
        }
        catch (Exception ex) when (ex is EntityFrameworkEncryptionException or FormatException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the rows with encrypted values in pages ordered by primary key, the reader closed before the rows are
    /// processed: a long-running scan would hold locks the updates wait for (SQL Server) or an old snapshot (PostgreSQL).
    /// Tables without a primary key are read in one go (key usage only).
    /// </summary>
    private static async Task ScanAsync(
        DbContext context, EncryptedTable table, Func<object[], object?[], ValueTask> onRow, CancellationToken cancellationToken)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var keys = table.KeyColumns.Select(x => sql.DelimitIdentifier(x.Name)).ToList();

        // keys, encrypted values, then blind indexes (NULL for columns without one)
        var columns = string.Join(", ", keys
            .Concat(table.Columns.Select(x => sql.DelimitIdentifier(x.Name)))
            .Concat(table.Columns.Select(x => x.Index is { } index ? sql.DelimitIdentifier(index.Store.Name) : "NULL")));
        var notNull = $"({string.Join(" OR ", table.Columns.Select(x => $"{sql.DelimitIdentifier(x.Name)} IS NOT NULL"))})";
        var paged = keys.Count > 0;
        object[]? last = null;

        while (true)
        {
            var page = new List<(object[] Keys, object?[] Values)>();
            await context.Database.OpenConnectionAsync(cancellationToken);

            try
            {
                await using var command = context.Database.GetDbConnection().CreateCommand();
                command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;

                var where = notNull;

                // after the last row of the previous page: k0 > @k0 OR (k0 = @k0 AND k1 > @k1) ...
                if (last != null)
                {
                    var after = keys.Select((_, i) => string.Join(" AND ", keys.Take(i + 1).Select((key, j) =>
                        $"{key} {(j == i ? ">" : "=")} {sql.GenerateParameterNamePlaceholder($"k{j}")}")));
                    where += $" AND ({string.Join(" OR ", after.Select(x => $"({x})"))})";

                    for (var i = 0; i < last.Length; i++)
                        AddParameter(command.Parameters, command, sql, $"k{i}", table.KeyColumns[i].Mapping, last[i]);
                }

                var from = $"FROM {sql.DelimitIdentifier(table.Name, table.Schema)} WHERE {where}";

                command.CommandText = !paged ? $"SELECT {columns} {from}"
                    : context.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer"
                        ? $"SELECT TOP ({PageSize}) {columns} {from} ORDER BY {string.Join(", ", keys)}"
                        : $"SELECT {columns} {from} ORDER BY {string.Join(", ", keys)} LIMIT {PageSize}";

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    var rowKeys = new object[keys.Count];
                    var values = new object?[table.Columns.Count * 2];

                    for (var i = 0; i < rowKeys.Length; i++)
                        rowKeys[i] = reader.GetValue(i);

                    for (var i = 0; i < values.Length; i++)
                        values[i] = reader.IsDBNull(rowKeys.Length + i) ? null : reader.GetValue(rowKeys.Length + i);

                    page.Add((rowKeys, values));
                }
            }
            finally
            {
                await context.Database.CloseConnectionAsync();
            }

            foreach (var (rowKeys, values) in page)
                await onRow(rowKeys, values);

            if (!paged || page.Count < PageSize)
                return;

            last = page[^1].Keys;
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

                // type mappings create parameters through a DbCommand; they are then added to the batch commands
                await using var factory = connection.CreateCommand();

                foreach (var update in updates)
                {
                    var command = batch.CreateBatchCommand();
                    command.CommandText = update.ToSql(sql);
                    update.AddParameters(command.Parameters, factory, sql);
                    batch.BatchCommands.Add(command);
                }

                await batch.ExecuteNonQueryAsync(ct);
                updated = batch.BatchCommands.Where((_, i) => updates[i].Counted).Sum(x => (long)x.RecordsAffected);
            }
            else
            {
                foreach (var update in updates)
                {
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction.GetDbTransaction();
                    command.CommandText = update.ToSql(sql);
                    update.AddParameters(command.Parameters, command, sql);

                    var affected = await command.ExecuteNonQueryAsync(ct);
                    updated += update.Counted ? affected : 0;
                }
            }

            await transaction.CommitAsync(ct);
            return (updated, updates.Count(x => x.Counted) - updated);
        }, cancellationToken);

    private static void AddParameter(
        DbParameterCollection parameters, DbCommand factory, ISqlGenerationHelper sql, string name, RelationalTypeMapping mapping, object value)
    {
        // configured without a value: values are as read from or written to the column, the converter of the
        // mapping (e.g. of a strongly typed id) must not run on them
        var parameter = mapping.CreateParameter(factory, sql.GenerateParameterName(name), null, nullable: false);
        parameter.Value = value;
        parameters.Add(parameter);
    }

    /// <summary>Column with its type mapping: parameters are typed like the column (e.g. datetime2, varchar, enums).</summary>
    private sealed record StoreColumn(string Name, RelationalTypeMapping Mapping)
    {
        public StoreColumn(IColumn column)
            : this(column.Name, column.StoreTypeMapping)
        { }
    }

    private sealed record EncryptedColumn(StoreColumn Store, IEncryptionConverter Converter, BlindIndexColumn? Index)
    {
        public string Name => Store.Name;
    }

    private sealed record BlindIndexColumn(StoreColumn Store, BlindIndexer Indexer);

    private sealed record EncryptedTable(string Name, string? Schema, IReadOnlyList<StoreColumn> KeyColumns, IReadOnlyList<EncryptedColumn> Columns)
    {
        public string DisplayName => Schema == null ? Name : $"{Schema}.{Name}";

        private static bool IsEncrypted(IProperty property)
            => property.GetValueConverter() is IEncryptionConverter;

        private static bool HasEncrypted(IComplexType type)
            => type.GetProperties().Any(IsEncrypted) || type.GetComplexProperties().Any(x => HasEncrypted(x.ComplexType));

        private static BlindIndexColumn? FindBlindIndex(ITable table, IColumn column)
        {
            var store = StoreObjectIdentifier.Table(table.Name, table.Schema);

            return column.PropertyMappings
                .Select(x => BlindIndex.Find(x.Property))
                .FirstOrDefault(x => x != null) is { } index
                ? new BlindIndexColumn(new StoreColumn(table.FindColumn(index.GetColumnName(store)!)!), ((IBlindIndexConverter)index.GetValueConverter()!).Indexer)
                : null;
        }

        public static IEnumerable<EncryptedTable> From(DbContext context, ILogger logger)
        {
            var model = context.Model;

            // only table columns are scanned: key usage would miss values stored elsewhere
            foreach (var entityType in model.GetEntityTypes())
            {
                if (!entityType.GetDeclaredProperties().Any(IsEncrypted) && !entityType.GetDeclaredComplexProperties().Any(x => HasEncrypted(x.ComplexType)))
                    continue;

                if (entityType.IsMappedToJson() || entityType.GetDeclaredComplexProperties().Any(x => x.ComplexType.IsMappedToJson() && HasEncrypted(x.ComplexType)))
                    logger.LogWarning("{Entity} has encrypted values mapped to JSON: they are not scanned", entityType.DisplayName());
                else if (entityType.GetTableName() == null)
                    logger.LogWarning("{Entity} isn't mapped to a table (e.g. a view or a SQL query): its encrypted values are not scanned",
                        entityType.DisplayName());
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
                        columns.Add(new EncryptedColumn(new StoreColumn(column), converters[0], FindBlindIndex(table, column)));
                }

                if (columns.Count > 0)
                    yield return new EncryptedTable(
                        table.Name,
                        table.Schema,
                        table.PrimaryKey?.Columns.Select(x => new StoreColumn(x)).ToList() ?? [],
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
    /// <param name="Counted">Counted in the result: <c>false</c> for blind indexes set along with re-encrypted values.</param>
    private sealed record Update(
        EncryptedTable Table, StoreColumn Column, StoreColumn CheckedColumn, object[] Keys, object OldValue, object NewValue, bool Counted = true)
    {
        public string ToSql(ISqlGenerationHelper sql)
        {
            var keys = Table.KeyColumns.Select((x, i) => $"{sql.DelimitIdentifier(x.Name)} = {sql.GenerateParameterNamePlaceholder($"k{i}")}");

            return $"UPDATE {sql.DelimitIdentifier(Table.Name, Table.Schema)} " +
                   $"SET {sql.DelimitIdentifier(Column.Name)} = {sql.GenerateParameterNamePlaceholder("new_value")} " +
                   $"WHERE {string.Join(" AND ", keys)} AND {sql.DelimitIdentifier(CheckedColumn.Name)} = {sql.GenerateParameterNamePlaceholder("old_value")}";
        }

        /// <param name="factory">Creates the parameters, configured by the type mapping of their column.</param>
        public void AddParameters(DbParameterCollection parameters, DbCommand factory, ISqlGenerationHelper sql)
        {
            Add("new_value", Column.Mapping, NewValue);
            Add("old_value", CheckedColumn.Mapping, OldValue);

            for (var i = 0; i < Keys.Length; i++)
                Add($"k{i}", Table.KeyColumns[i].Mapping, Keys[i]);

            void Add(string name, RelationalTypeMapping mapping, object value)
                => AddParameter(parameters, factory, sql, name, mapping, value);
        }
    }
}
