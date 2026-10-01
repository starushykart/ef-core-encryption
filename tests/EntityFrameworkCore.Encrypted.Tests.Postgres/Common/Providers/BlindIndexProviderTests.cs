using System.Security.Cryptography;
using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Storage;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Providers;

/// <summary>Blind indexes on every relational provider: queries, updates, key rotation and rebuilding. Each test uses its own database.</summary>
public abstract class BlindIndexProviderTests(ITestOutputHelper helper) : IAsyncLifetime
{
    private readonly InMemoryKeyWrapper _wrapper = new();
    private string _connectionString = null!;
    private ServiceProvider _provider = null!;

    protected abstract string CreateConnectionString();

    protected abstract void UseProvider(DbContextOptionsBuilder options, string connectionString);

    public async ValueTask InitializeAsync()
    {
        _connectionString = CreateConnectionString();
        _provider = Build(x => x.UseKeyWrapper(_ => _wrapper));

        await using (var scope = _provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>().Database.EnsureCreatedAsync();

        await _provider.InitializeEncryptionAsync();
    }

    [Fact]
    public async Task Should_find_values_by_equality()
    {
        await AddPeopleAsync(_provider);
        var email = "  JANE@Example.com ";
        var ssn = "111-22-3333";
        var document = new byte[] { 1, 2, 3 };

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();

        (await db.People.SingleAsync(x => x.Email == email)).Name.Should().Be("jane");
        (await db.People.SingleAsync(x => x.Ssn == ssn)).Name.Should().Be("jane");
        (await db.People.SingleAsync(x => x.Document == document)).Name.Should().Be("jane");
        (await db.People.Where(x => x.Status == PersonStatus.Blocked).Select(x => x.Name).ToListAsync()).Should().Equal("john");
        (await db.People.Where(x => x.Ssn != ssn).Select(x => x.Name).ToListAsync()).Should().BeEquivalentTo("john", "max");
        (await db.People.CountAsync(x => x.Ssn == "111-22-3333 ")).Should().Be(0, "only the email is normalized");
    }

    [Fact]
    public async Task Should_find_values_in_list()
    {
        await AddPeopleAsync(_provider);
        string[] emails = ["jane@example.com", "MAX@example.com"];

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();

        (await db.People.Where(x => emails.Contains(x.Email)).Select(x => x.Name).ToListAsync()).Should().BeEquivalentTo("jane", "max");
    }

    [Fact]
    public async Task Should_update_blind_index_when_value_changes()
    {
        var people = await AddPeopleAsync(_provider);

        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();
            var jane = await db.People.SingleAsync(x => x.Id == people[0].Id);
            jane.Email = "jane.doe@example.com";
            jane.Status = PersonStatus.Active;
            await db.SaveChangesAsync();
        }

        // disconnected entity: all properties are saved
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();
            db.Update(people[1] with { Ssn = null });
            await db.SaveChangesAsync();
        }

        await using var verify = _provider.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<BlindIndexDbContext>();

        (await context.People.AnyAsync(x => x.Email == "jane@example.com")).Should().BeFalse();
        (await context.People.SingleAsync(x => x.Email == "Jane.Doe@example.com")).Name.Should().Be("jane");
        (await context.People.Where(x => x.Status == PersonStatus.Active).Select(x => x.Name).ToListAsync()).Should().BeEquivalentTo("jane", "max");
        (await context.People.AnyAsync(x => x.Ssn == "222-33-4444")).Should().BeFalse();
        (await context.People.SingleAsync(x => x.Ssn == null)).Name.Should().Be("john");
    }

    [Fact]
    public async Task Should_update_blind_index_in_every_update_path()
    {
        var people = await AddPeopleAsync(_provider);

        // tracked entity, synchronous SaveChanges
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();
            db.People.Single(x => x.Id == people[0].Id).Email = "jane.sync@example.com";
            db.SaveChanges();
        }

        // detached entity with a new value: Update marks every property as modified
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();
            db.Update(people[1] with { Email = "john.detached@example.com" });
            await db.SaveChangesAsync();
        }

        // attached entity with only the encrypted property marked as modified
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();
            var max = people[2] with { Email = "max.attached@example.com" };
            db.Attach(max).Property(x => x.Email).IsModified = true;
            await db.SaveChangesAsync();
        }

        // another property changes: the blind index stays valid
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();
            (await db.People.SingleAsync(x => x.Id == people[0].Id)).Name = "jane doe";
            await db.SaveChangesAsync();
        }

        await using var verify = _provider.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<BlindIndexDbContext>();

        (await context.People.SingleAsync(x => x.Email == "jane.sync@example.com")).Name.Should().Be("jane doe");
        (await context.People.SingleAsync(x => x.Email == "john.detached@example.com")).Name.Should().Be("john");
        (await context.People.SingleAsync(x => x.Email == "max.attached@example.com")).Name.Should().Be("max");
        (await context.People.AnyAsync(x => x.Email == "jane@example.com" || x.Email == "john@example.com" || x.Email == "max@example.com"))
            .Should().BeFalse("previous values are no longer indexed");
    }

    [Fact]
    public async Task Should_update_blind_index_with_execute_update()
    {
        await AddPeopleAsync(_provider);
        var email = "new@example.com";

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();

        (await db.People.Where(x => x.Name == "jane").ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, email))).Should().Be(1);

        (await db.People.SingleAsync(x => x.Email == "NEW@example.com")).Email.Should().Be(email);
        (await db.People.AnyAsync(x => x.Email == "jane@example.com")).Should().BeFalse();

        (await db.People.Where(x => x.Name == "john").ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, (string?)null))).Should().Be(1);
        (await db.People.SingleAsync(x => x.Email == null)).Name.Should().Be("john");
        (await db.People.AnyAsync(x => x.Email == "john@example.com")).Should().BeFalse();
        (await db.People.CountAsync(x => EF.Property<string?>(x, "Email_Index") == null)).Should().Be(1, "the index is cleared too");

        var fromExpression = () => db.People.ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, x => x.Name));
        await fromExpression.Should().ThrowAsync<EntityFrameworkEncryptionException>().WithMessage("*blind index*");
    }

    [Fact]
    public async Task Should_delete_by_blind_index()
    {
        await AddPeopleAsync(_provider);

        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();

        (await db.People.Where(x => x.Email == "john@example.com").ExecuteDeleteAsync()).Should().Be(1);
        (await db.People.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Should_keep_finding_values_after_key_rotation_re_encryption_and_rewrap()
    {
        await AddPeopleAsync(_provider);

        await _provider.RotateRootKeyAsync<BlindIndexDbContext>();
        await using (var scope = _provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();
            db.Add(new Person { Id = Guid.NewGuid(), Name = "ann", Email = "ann@example.com", Status = PersonStatus.Active });
            await db.SaveChangesAsync();
        }

        await _provider.ReEncryptAsync<BlindIndexDbContext>();

        _wrapper.UseNewMasterKey("new-master-key");
        await _provider.RewrapRootKeysAsync<BlindIndexDbContext>();
        _wrapper.Disable(InMemoryKeyWrapper.WrappingKeyId);

        await using var restarted = Build(x => x.UseKeyWrapper(_ => _wrapper));
        await restarted.InitializeEncryptionAsync();
        await using var verify = restarted.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<BlindIndexDbContext>();

        (await context.People.SingleAsync(x => x.Email == "jane@example.com")).Name.Should().Be("jane");
        (await context.People.SingleAsync(x => x.Email == "ann@example.com")).Name.Should().Be("ann");
        (await GetStoredKeysAsync()).Should().OnlyContain(x => x.WrappingKeyId == "new-master-key");
    }

    [Fact]
    public async Task Should_keep_finding_values_after_static_key_rotation()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);

        await using (var initial = Build(x => x.UseKey(oldKey)))
        {
            await ResetKeysAsync();
            await initial.InitializeEncryptionAsync();
            await AddPeopleAsync(initial);
        }

        // both keys: the blind index key is rewrapped with the new one, values are re-encrypted
        await using (var rotating = Build(x => x.UseKey(oldKey).UseKey(newKey, id: 2)))
        {
            await rotating.InitializeEncryptionAsync();
            await rotating.ReEncryptAsync<BlindIndexDbContext>();
        }

        await using var rotated = Build(x => x.UseKey(newKey, id: 2));
        await rotated.InitializeEncryptionAsync();
        await using var scope = rotated.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();

        (await db.People.SingleAsync(x => x.Email == "jane@example.com")).Ssn.Should().Be("111-22-3333");
        (await GetStoredKeysAsync()).Should().ContainSingle().Which.WrappingKeyId.Should().Be("static:2");
    }

    [Fact]
    public async Task Should_create_single_blind_index_key_when_instances_start_concurrently()
    {
        await ResetKeysAsync();
        var instances = Enumerable.Range(0, 5).Select(_ => Build(x => x.UseKeyWrapper(_ => _wrapper))).ToList();

        await Task.WhenAll(instances.Select(x => x.InitializeEncryptionAsync()));

        (await GetStoredKeysAsync()).Select(x => x.Id).Should().BeEquivalentTo([0, 1]);
        foreach (var instance in instances)
            await instance.DisposeAsync();
    }

    [Fact]
    public async Task Should_rebuild_missing_blind_indexes()
    {
        await AddPeopleAsync(_provider);

        // as after adding a blind index to a column with existing values
        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>().People
                .ExecuteUpdateAsync(s => s.SetProperty(x => EF.Property<string?>(x, "Email_Index"), (string?)null));
        }

        (await _provider.RebuildBlindIndexesAsync<BlindIndexDbContext>(batchSize: 2)).Should().Be(3);
        (await _provider.RebuildBlindIndexesAsync<BlindIndexDbContext>()).Should().Be(0, "indexes are up to date");

        await using var verify = _provider.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<BlindIndexDbContext>().People.SingleAsync(x => x.Email == "max@example.com"))
            .Name.Should().Be("max");
    }

    public async ValueTask DisposeAsync()
    {
        await using (var scope = _provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>().Database.EnsureDeletedAsync();

        await _provider.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private ServiceProvider Build(Action<EncryptionBuilder> configure)
        => new ServiceCollection()
            .AddEncryption(configure)
            .AddDbContext<BlindIndexDbContext>(x => UseProvider(x.UseEncryption(), _connectionString))
            .AddXunitLogging(helper)
            .BuildServiceProvider(true);

    private static async Task<List<Person>> AddPeopleAsync(IServiceProvider provider)
    {
        List<Person> people =
        [
            new() { Id = Guid.NewGuid(), Name = "jane", Email = "jane@example.com", Ssn = "111-22-3333", Status = PersonStatus.Pending, Document = [1, 2, 3] },
            new() { Id = Guid.NewGuid(), Name = "john", Email = "john@example.com", Ssn = "222-33-4444", Status = PersonStatus.Blocked },
            new() { Id = Guid.NewGuid(), Name = "max", Email = "max@example.com", Ssn = "333-44-5555", Status = PersonStatus.Active }
        ];

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>();
        db.AddRange(people);
        await db.SaveChangesAsync();

        return people;
    }

    private async Task ResetKeysAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>().Set<EncryptionKeyEntity>().ExecuteDeleteAsync();
    }

    private async Task<List<EncryptionKeyEntity>> GetStoredKeysAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BlindIndexDbContext>().Set<EncryptionKeyEntity>().AsNoTracking().ToListAsync();
    }
}

public sealed class BlindIndexDbContext(DbContextOptions<BlindIndexDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Person>(e =>
        {
            e.Property(x => x.Email).IsEncrypted().HasBlindIndex(v => v.Trim().ToLowerInvariant());
            e.Property(x => x.Status).HasConversion<string>().IsEncrypted().HasBlindIndex();
        });
}

public sealed record Person
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;

    /// <summary>Normalized: found regardless of case and surrounding spaces.</summary>
    public string? Email { get; set; }

    [Encrypted, BlindIndex]
    public string? Ssn { get; set; }

    /// <summary>Enum converted to text, then encrypted and indexed.</summary>
    public PersonStatus Status { get; set; }

    [Encrypted, BlindIndex]
    public byte[]? Document { get; set; }
}

public enum PersonStatus { Pending, Active, Blocked }
