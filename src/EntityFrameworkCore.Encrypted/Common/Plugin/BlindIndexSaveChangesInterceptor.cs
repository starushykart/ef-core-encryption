using System.Runtime.CompilerServices;
using EntityFrameworkCore.Encrypted.Common.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

/// <summary>
/// Copies the values of encrypted properties with a blind index into their <c>{Property}_Index</c> shadow properties
/// before saving, for added entities and changed values; the converter of the shadow property hashes them.
/// </summary>
internal sealed class BlindIndexSaveChangesInterceptor : ISaveChangesInterceptor
{
    private static readonly ConditionalWeakTable<IModel, Dictionary<IEntityType, (string Source, string Index)[]>> Cache = new();

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
            Fill(context);

        return result;
    }

    public async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && Fill(context))
        {
            // load before EF starts the transaction: creating the key inside it would wait for its locks (SQLite)
            if (KeyRing(context) is { } keyRing)
                await keyRing.LoadIndexKeysAsync(context.GetType(), cancellationToken);
        }

        return result;
    }

    /// <returns><c>true</c> if a blind index was set.</returns>
    private static bool Fill(DbContext context)
    {
        var indexes = Cache.GetValue(context.Model, Find);

        if (indexes.Count == 0)
            return false;

        // SaveChanges detects changes after this interceptor runs
        if (context.ChangeTracker.AutoDetectChangesEnabled)
            context.ChangeTracker.DetectChanges();

        var filled = false;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified) || !indexes.TryGetValue(entry.Metadata, out var properties))
                continue;

            foreach (var (source, index) in properties)
            {
                var value = entry.Property(source);

                if (entry.State == EntityState.Added || value.IsModified)
                {
                    Set(entry.Property(index), value.CurrentValue, entry.State);
                    filled = true;
                }
            }
        }

        return filled;
    }

    private static void Set(PropertyEntry index, object? value, EntityState state)
    {
        index.CurrentValue = value;

        // the stored hash is never read back: mark it explicitly, also when the value equals the default
        if (state == EntityState.Modified)
            index.IsModified = true;
    }

    private static Dictionary<IEntityType, (string Source, string Index)[]> Find(IModel model)
        => model.GetEntityTypes()
            .Select(type => (Type: type, Indexes: type.GetProperties()
                .Where(x => x.GetValueConverter() is IBlindIndexConverter)
                .Select(x => (((IBlindIndexConverter)x.GetValueConverter()!).SourceProperty, x.Name))
                .ToArray()))
            .Where(x => x.Indexes.Length > 0)
            .ToDictionary(x => x.Type, x => x.Indexes);

    private static DataKeyRing? KeyRing(DbContext context)
        => context.GetService<IDbContextOptions>().FindExtension<EncryptionDbContextOptionsExtension>()?.KeyRing;
}
