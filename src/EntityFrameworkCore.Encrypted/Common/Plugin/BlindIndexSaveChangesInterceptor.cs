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
/// when saving, for added entities and changed values; the converter of the shadow property hashes them.
/// </summary>
/// <remarks>
/// Filled before saving, and again when SaveChanges detects changes, which happens after every interceptor: values
/// changed by interceptors registered after this one (e.g. normalization, auditing) are indexed as saved.
/// </remarks>
internal sealed class BlindIndexSaveChangesInterceptor : ISaveChangesInterceptor
{
    private static readonly ConditionalWeakTable<IModel, Dictionary<IEntityType, (string Source, string Index)[]>> Cache = new();

    // change trackers whose detected changes refill the blind indexes while their context saves
    private static readonly ConditionalWeakTable<ChangeTracker, SaveState> States = new();

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
            Start(context);

        return result;
    }

    public async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context && Start(context))
        {
            // load before EF starts the transaction: creating the key inside it would wait for its locks (SQLite)
            if (KeyRing(context) is { } keyRing)
                await keyRing.LoadIndexKeysAsync(context.GetType(), cancellationToken);
        }

        return result;
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Stop(eventData.Context);
        return result;
    }

    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Stop(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData)
        => Stop(eventData.Context);

    public Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Stop(eventData.Context);
        return Task.CompletedTask;
    }

    public void SaveChangesCanceled(DbContextEventData eventData)
        => Stop(eventData.Context);

    public Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Stop(eventData.Context);
        return Task.CompletedTask;
    }

    /// <returns><c>true</c> if a blind index was set: the key store saves through the same context type, without them.</returns>
    private static bool Start(DbContext context)
    {
        EncryptedModel.EnsureBuiltWithEncryption(context);

        if (GetIndexes(context).Count == 0)
            return false;

        var tracker = context.ChangeTracker;

        if (!States.TryGetValue(tracker, out var state))
        {
            state = States.GetValue(tracker, _ => new SaveState());

            // pooled contexts keep their change tracker: subscribed once
            lock (state)
            {
                if (!state.Subscribed)
                {
                    tracker.DetectedAllChanges += OnDetectedAllChanges;
                    state.Subscribed = true;
                }
            }
        }

        state.Saving = true;

        // SaveChanges detects changes after the interceptors: detect them now to see what was changed
        if (tracker.AutoDetectChangesEnabled)
            tracker.DetectChanges();

        return Fill(context);
    }

    private static void Stop(DbContext? context)
    {
        if (context != null && States.TryGetValue(context.ChangeTracker, out var state))
            state.Saving = false;
    }

    private static void OnDetectedAllChanges(object? sender, DetectedChangesEventArgs e)
    {
        if (sender is ChangeTracker tracker && States.TryGetValue(tracker, out var state) && state.Saving)
            _ = Fill(tracker.Context);
    }

    private static bool Fill(DbContext context)
    {
        var indexes = GetIndexes(context);
        var filled = false;
        var tracker = context.ChangeTracker;
        var autoDetectChanges = tracker.AutoDetectChangesEnabled;

        // Entries() would detect changes again, which raises DetectedAllChanges
        tracker.AutoDetectChangesEnabled = false;

        try
        {
            foreach (var entry in tracker.Entries())
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
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = autoDetectChanges;
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

    private static Dictionary<IEntityType, (string Source, string Index)[]> GetIndexes(DbContext context)
        => Cache.GetValue(context.Model, static model => model.GetEntityTypes()
            .Select(type => (Type: type, Indexes: type.GetProperties()
                .Where(x => x.GetValueConverter() is IBlindIndexConverter)
                .Select(x => (((IBlindIndexConverter)x.GetValueConverter()!).SourceProperty, x.Name))
                .ToArray()))
            .Where(x => x.Indexes.Length > 0)
            .ToDictionary(x => x.Type, x => x.Indexes));

    private static DataKeyRing? KeyRing(DbContext context)
        => context.GetService<IDbContextOptions>().FindExtension<EncryptionDbContextOptionsExtension>()?.KeyRing;

    private sealed class SaveState
    {
        public bool Subscribed { get; set; }

        public volatile bool Saving;
    }
}
