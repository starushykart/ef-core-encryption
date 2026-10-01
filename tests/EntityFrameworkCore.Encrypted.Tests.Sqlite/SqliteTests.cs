using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Storage;
using EntityFrameworkCore.Encrypted.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Sqlite;

/// <summary>
/// Key store and data maintenance on a second database: SQL built from the relational model (identifier quoting,
/// parameters, composite keys) and the per-command update path, as SQLite doesn't support <c>DbBatch</c>.
/// </summary>
public sealed class SqliteTests(ITestOutputHelper helper) : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source={Path.Combine(Path.GetTempPath(), $"efenc_{Guid.NewGuid():N}.db")}";
    private readonly InMemoryKeyWrapper _wrapper = new();
    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        _provider = Build();

        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SqliteDbContext>();
        await context.Database.EnsureCreatedAsync();

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
        await _provider.RotateRootKeyAsync<SqliteDbContext>();

        var before = await _provider.GetKeyUsageAsync<SqliteDbContext>();
        var result = await _provider.ReEncryptAsync<SqliteDbContext>(batchSize: 2);
        var after = await _provider.GetKeyUsageAsync<SqliteDbContext>();

        before.Should().BeEquivalentTo(new[]
        {
            new KeyUsage("Notes", nameof(Note.Attachment), 1, 0, 5),
            new KeyUsage("Notes", nameof(Note.Text), 1, 0, 5),
            new KeyUsage("Tags", nameof(Tag.Value), 1, 0, 3)
        });
        result.Should().Be(new ReEncryptionResult(ReEncrypted: 13, Skipped: 0, Invalid: 0));
        after.Should().OnlyContain(x => x.RootKeyId == 2);
        await AssertReadableAsync(_provider, notes, tags);
    }

    [Fact]
    public async Task Should_rewrap_root_keys()
    {
        var notes = await AddNotesAsync(2);

        _wrapper.UseNewMasterKey("new-master-key");
        (await _provider.RewrapRootKeysAsync<SqliteDbContext>()).Should().Be(1);
        _wrapper.Disable(InMemoryKeyWrapper.WrappingKeyId);

        await using var restarted = Build();
        await AssertReadableAsync(restarted, notes);
    }

    public async ValueTask DisposeAsync()
    {
        await using (var scope = _provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SqliteDbContext>().Database.EnsureDeletedAsync();

        await _provider.DisposeAsync();
    }

    private ServiceProvider Build()
        => new ServiceCollection()
            .AddEncryption(x => x.UseKeyWrapper(_ => _wrapper))
            .AddDbContext<SqliteDbContext>(x => x.UseSqlite(_connectionString).UseEncryption())
            .AddXunitLogging(helper)
            .BuildServiceProvider(true);

    private async Task<List<Note>> AddNotesAsync(int count)
    {
        var notes = Enumerable.Range(0, count)
            .Select(i => new Note { Id = Guid.NewGuid(), Text = $"note {i}", Attachment = [(byte)i, 42] })
            .ToList();

        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SqliteDbContext>();
        context.AddRange(notes);
        await context.SaveChangesAsync();
        return notes;
    }

    private async Task<List<Tag>> AddTagsAsync(int count)
    {
        var tags = Enumerable.Range(0, count).Select(i => new Tag { Group = "g", Name = $"tag {i}", Value = $"value {i}" }).ToList();

        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SqliteDbContext>();
        context.AddRange(tags);
        await context.SaveChangesAsync();
        return tags;
    }

    private static async Task AssertReadableAsync(IServiceProvider provider, IEnumerable<Note> notes, IEnumerable<Tag>? tags = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SqliteDbContext>();

        (await context.Notes.AsNoTracking().ToListAsync()).Should().BeEquivalentTo(notes);

        if (tags != null)
            (await context.Tags.AsNoTracking().ToListAsync()).Should().BeEquivalentTo(tags);
    }

    private async Task<List<EncryptionKeyEntity>> GetStoredKeysAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SqliteDbContext>().Set<EncryptionKeyEntity>().AsNoTracking().ToListAsync();
    }
}

public sealed class SqliteDbContext(DbContextOptions<SqliteDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Tag>().HasKey(x => new { x.Group, x.Name });
}

public sealed class Note
{
    public Guid Id { get; set; }

    [Encrypted]
    public string? Text { get; set; }

    [Encrypted]
    public byte[]? Attachment { get; set; }
}

/// <summary>Composite primary key.</summary>
public sealed class Tag
{
    public string Group { get; set; } = null!;
    public string Name { get; set; } = null!;

    [Encrypted]
    public string? Value { get; set; }
}
