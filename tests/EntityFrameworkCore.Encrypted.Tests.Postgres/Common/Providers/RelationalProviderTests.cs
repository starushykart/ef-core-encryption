using System.Transactions;
using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Storage;
using EntityFrameworkCore.Encrypted.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Providers;

/// <summary>
/// Same scenarios on every relational provider: the key store, and data maintenance with SQL built from the
/// relational model (identifier quoting, parameters, composite keys, batched or per-command updates, locking).
/// Each test uses its own database.
/// </summary>
public abstract class RelationalProviderTests(ITestOutputHelper helper) : IAsyncLifetime
{
    private readonly InMemoryKeyWrapper _wrapper = new();
    private string _connectionString = null!;
    private ServiceProvider _provider = null!;

    /// <summary>Connection string of a new, empty database.</summary>
    protected abstract string CreateConnectionString();

    protected abstract void UseProvider(DbContextOptionsBuilder options, string connectionString);

    /// <summary>Several connections in one TransactionScope, e.g. the key store's and the application's.</summary>
    protected virtual bool SupportsConcurrentConnectionsInTransactionScope => true;

    public async ValueTask InitializeAsync()
    {
        _connectionString = CreateConnectionString();
        _provider = Build();

        await using (var scope = _provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ProviderDbContext>().Database.EnsureCreatedAsync();

        // as the generic host does on start: SQLite locks the whole database while saving, so creating the root key
        // on first use inside SaveChanges would wait for the save to finish
        await _provider.InitializeEncryptionAsync();
    }

    [Fact]
    public async Task Should_store_root_key_and_read_values_after_restart()
    {
        var notes = await AddNotesAsync(3);

        await using var restarted = Build();

        await AssertReadableAsync(restarted, notes);
        (await GetStoredKeysAsync()).Should().ContainSingle().Which.WrappingKeyId.Should().Be(InMemoryKeyWrapper.WrappingKeyId);
        _wrapper.UnwrapCalls.Should().Be(1);
    }

    [Fact]
    public async Task Should_create_single_root_key_when_instances_start_concurrently()
    {
        await ResetKeysAsync();
        var instances = Enumerable.Range(0, 5).Select(_ => Build()).ToList();

        await Task.WhenAll(instances.Select(x => x.InitializeEncryptionAsync()));

        (await GetStoredKeysAsync()).Should().ContainSingle();
        foreach (var instance in instances)
            await instance.DisposeAsync();
    }

    [Fact]
    public async Task Should_report_key_usage_and_re_encrypt_with_active_key()
    {
        var notes = await AddNotesAsync(5);
        var tags = await AddTagsAsync(3);
        await _provider.RotateRootKeyAsync<ProviderDbContext>();

        var before = await _provider.GetKeyUsageAsync<ProviderDbContext>();
        var result = await _provider.ReEncryptAsync<ProviderDbContext>(batchSize: 2);
        var after = await _provider.GetKeyUsageAsync<ProviderDbContext>();

        before.Should().BeEquivalentTo(new[]
        {
            new KeyUsage("Notes", nameof(Note.Attachment), 1, 0, 5),
            new KeyUsage("Notes", "Author_Name", 1, 0, 5),
            new KeyUsage("Notes", nameof(Note.Status), 1, 0, 5),
            new KeyUsage("Notes", nameof(Note.Text), 1, 0, 5),
            new KeyUsage("Tags", nameof(Tag.Value), 1, 0, 3)
        });
        result.Should().Be(new ReEncryptionResult(ReEncrypted: 23, Skipped: 0, Invalid: 0));
        after.Should().OnlyContain(x => x.RootKeyId == 2);
        await AssertReadableAsync(_provider, notes, tags);
    }

    [Fact]
    public async Task Should_re_encrypt_rows_with_precise_and_converted_keys()
    {
        // microseconds (the precision of every provider): lost if the key parameter isn't typed like the column
        var at = new DateTime(2026, 10, 1, 12, 30, 45, DateTimeKind.Utc).AddTicks(1234560);
        var readings = Enumerable.Range(0, 5)
            .Select(i => new Reading { Sensor = new SensorId($"s{i}"), At = at.AddTicks(i * 10), Value = $"value {i}" })
            .ToList();

        await using (var scope = _provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
            context.AddRange(readings);
            await context.SaveChangesAsync();
        }

        await _provider.RotateRootKeyAsync<ProviderDbContext>();

        (await _provider.ReEncryptAsync<ProviderDbContext>(batchSize: 2)).Should().Be(new ReEncryptionResult(ReEncrypted: 5, Skipped: 0, Invalid: 0));
        (await _provider.GetKeyUsageAsync<ProviderDbContext>()).Should().OnlyContain(x => x.RootKeyId == 2);

        await using var verify = _provider.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<ProviderDbContext>().Readings.AsNoTracking().ToListAsync())
            .Should().BeEquivalentTo(readings);
    }

    [Fact]
    public async Task Should_re_encrypt_tables_larger_than_a_page()
    {
        // pages of 1,000 rows by composite key: several rows per sensor, precise timestamps as the second key column
        var at = new DateTime(2026, 10, 1, 12, 30, 45, DateTimeKind.Utc).AddTicks(1234560);
        var readings = Enumerable.Range(0, 2500)
            .Select(i => new Reading { Sensor = new SensorId($"s{i % 3}"), At = at.AddTicks(i * 10), Value = $"value {i}" })
            .ToList();

        await using (var scope = _provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
            context.AddRange(readings);
            await context.SaveChangesAsync();
        }

        await _provider.RotateRootKeyAsync<ProviderDbContext>();

        (await _provider.ReEncryptAsync<ProviderDbContext>(batchSize: 300)).Should().Be(new ReEncryptionResult(ReEncrypted: 2500, Skipped: 0, Invalid: 0));
        (await _provider.GetKeyUsageAsync<ProviderDbContext>()).Should().ContainSingle(x => x.Table.EndsWith("Readings"))
            .Which.Should().Match<KeyUsage>(x => x.RootKeyId == 2 && x.Values == 2500);
    }

    [Fact]
    public async Task Should_keep_keys_created_inside_a_rolled_back_transaction_scope()
    {
        Assert.SkipUnless(SupportsConcurrentConnectionsInTransactionScope, "one connection at a time can write");
        await ResetKeysAsync();

        // keys aren't loaded yet: the root key is created on first use, inside the application's transaction
        await using var provider = Build();

        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
            context.Add(new Note { Id = Guid.NewGuid(), Text = "rolled back", Author = new Author { Name = "a" } });
            await context.SaveChangesAsync();
        }

        (await GetStoredKeysAsync()).Should().ContainSingle("the key store doesn't take part in the application's transaction");

        var notes = new List<Note> { new() { Id = Guid.NewGuid(), Text = "kept", Author = new Author { Name = "b" } } };
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
            context.AddRange(notes);
            await context.SaveChangesAsync();
        }

        await using var restarted = Build();
        await AssertReadableAsync(restarted, notes);
    }

    [Fact]
    public async Task Should_re_encrypt_while_reading_a_larger_table()
    {
        // many batches are written while the scan of the same table is still open on another connection
        var notes = await AddNotesAsync(300);
        await _provider.RotateRootKeyAsync<ProviderDbContext>();

        var result = await _provider.ReEncryptAsync<ProviderDbContext>(batchSize: 25)
            .WaitAsync(TimeSpan.FromSeconds(60));

        result.Should().Be(new ReEncryptionResult(ReEncrypted: 1200, Skipped: 0, Invalid: 0));
        await AssertReadableAsync(_provider, notes);
    }

    [Fact]
    public async Task Should_not_overwrite_values_changed_while_re_encrypting()
    {
        var notes = await AddNotesAsync(3);
        await _provider.RotateRootKeyAsync<ProviderDbContext>();

        var changed = false;
        await using var provider = Build(services => services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(new CallbackLoggerProvider(message =>
        {
            // after the first batch: the application updates all rows, the scan still holds their previous values
            if (changed || !System.Text.RegularExpressions.Regex.IsMatch(message, "batch.*written", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return;

            changed = true;
            using var scope = _provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
            foreach (var note in context.Notes)
                note.Text = "changed";
            context.SaveChanges();
        }))));

        var result = await provider.ReEncryptAsync<ProviderDbContext>(batchSize: 1).WaitAsync(TimeSpan.FromSeconds(60));

        result.Skipped.Should().BeInRange(2, 3);
        (result.ReEncrypted + result.Skipped).Should().Be(12);
        await using var verify = _provider.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<ProviderDbContext>().Notes.Select(x => x.Text).ToListAsync())
            .Should().AllBe("changed").And.HaveCount(notes.Count);
    }

    [Fact]
    public async Task Should_rewrap_root_keys()
    {
        var notes = await AddNotesAsync(2);

        _wrapper.UseNewMasterKey("new-master-key");
        (await _provider.RewrapRootKeysAsync<ProviderDbContext>()).Should().Be(1);
        _wrapper.Disable(InMemoryKeyWrapper.WrappingKeyId);

        await using var restarted = Build();
        await AssertReadableAsync(restarted, notes);
    }

    public async ValueTask DisposeAsync()
    {
        await using (var scope = _provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ProviderDbContext>().Database.EnsureDeletedAsync();

        await _provider.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection()
            .AddEncryption(x => x.UseKeyWrapper(_ => _wrapper))
            .AddDbContext<ProviderDbContext>(x => UseProvider(x.UseEncryption(), _connectionString));

        configure?.Invoke(services);
        return services.AddXunitLogging(helper).BuildServiceProvider(true);
    }

    private async Task ResetKeysAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ProviderDbContext>().Set<EncryptionKeyEntity>().ExecuteDeleteAsync();
    }

    private async Task<List<Note>> AddNotesAsync(int count)
    {
        var notes = Enumerable.Range(0, count)
            .Select(i => new Note
            {
                Id = Guid.NewGuid(),
                Text = $"note {i}",
                Attachment = [(byte)i, 42],
                Status = (NoteStatus)(i % 2),
                Author = new Author { Name = $"author {i}" }
            })
            .ToList();

        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
        context.AddRange(notes);
        await context.SaveChangesAsync();
        return notes;
    }

    private async Task<List<Tag>> AddTagsAsync(int count)
    {
        var tags = Enumerable.Range(0, count).Select(i => new Tag { Group = "g", Name = $"tag {i}", Value = $"value {i}" }).ToList();

        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
        context.AddRange(tags);
        await context.SaveChangesAsync();
        return tags;
    }

    private static async Task AssertReadableAsync(IServiceProvider provider, IEnumerable<Note> notes, IEnumerable<Tag>? tags = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();

        (await context.Notes.AsNoTracking().ToListAsync()).Should().BeEquivalentTo(notes);

        if (tags != null)
            (await context.Tags.AsNoTracking().ToListAsync()).Should().BeEquivalentTo(tags);
    }

    private async Task<List<EncryptionKeyEntity>> GetStoredKeysAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ProviderDbContext>().Set<EncryptionKeyEntity>().AsNoTracking().ToListAsync();
    }

    private sealed class CallbackLoggerProvider(Action<string> onMessage) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
            => new CallbackLogger(onMessage);

        public void Dispose()
        { }

        private sealed class CallbackLogger(Action<string> onMessage) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel)
                => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => onMessage(formatter(state, exception));
        }
    }
}

public sealed class ProviderDbContext(DbContextOptions<ProviderDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Reading> Readings => Set<Reading>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>(e =>
        {
            e.ComplexProperty(x => x.Author);
            e.Property(x => x.Status).HasConversion<string>().IsEncrypted();
        });
        modelBuilder.Entity<Tag>().HasKey(x => new { x.Group, x.Name });
        modelBuilder.Entity<Reading>(e =>
        {
            e.HasKey(x => new { x.Sensor, x.At });
            e.Property(x => x.Sensor).HasConversion(x => x.Value, x => new SensorId(x)).HasMaxLength(20).IsUnicode(false);
        });
    }
}

/// <summary>
/// Key types whose parameters must be typed like the column: a high-precision timestamp (datetime2(7) on SQL Server),
/// and a value-converted, non-Unicode string.
/// </summary>
public sealed class Reading
{
    public SensorId Sensor { get; set; }
    public DateTime At { get; set; }

    [Encrypted]
    public string? Value { get; set; }
}

public readonly record struct SensorId(string Value);

public sealed class Note
{
    public Guid Id { get; set; }

    [Encrypted]
    public string? Text { get; set; }

    [Encrypted]
    public byte[]? Attachment { get; set; }

    /// <summary>Encrypted after the enum is converted to text.</summary>
    public NoteStatus Status { get; set; }

    /// <summary>Complex type with an encrypted property.</summary>
    public Author Author { get; set; } = new();
}

public enum NoteStatus { Draft, Published }

public sealed class Author
{
    [Encrypted]
    public string Name { get; set; } = null!;
}

/// <summary>Composite primary key.</summary>
public sealed class Tag
{
    public string Group { get; set; } = null!;
    public string Name { get; set; } = null!;

    [Encrypted]
    public string? Value { get; set; }
}
