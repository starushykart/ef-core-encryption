using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Sqlite;

/// <summary>Keys are stored through the application's own context type, with its interceptors.</summary>
public sealed class SqliteKeyStoreTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"efenc_store_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Should_create_keys_when_an_interceptor_adds_encrypted_values_on_save()
    {
        await using var provider = new ServiceCollection()
            .AddEncryption(x => x.UseKeyWrapper(_ => new InMemoryKeyWrapper()))
            .AddDbContext<AuditedDbContext>(x => x.UseSqlite($"Data Source={_path}").UseEncryption().AddInterceptors(new AuditInterceptor()))
            .BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AuditedDbContext>().Database.EnsureCreatedAsync();

        // the root key and the blind index key are created on startup: the audit interceptor must not run for them
        await provider.InitializeEncryptionAsync().WaitAsync(TimeSpan.FromSeconds(30));

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuditedDbContext>();
            db.Add(new AuditedCustomer { Email = "jane@example.com" });
            await db.SaveChangesAsync();
        }

        await using var verify = provider.CreateAsyncScope();
        var context = verify.ServiceProvider.GetRequiredService<AuditedDbContext>();

        (await context.Customers.SingleAsync(x => x.Email == "jane@example.com")).Should().NotBeNull();
        (await context.AuditEntries.Select(x => x.Payload).ToListAsync()).Should().Equal("added AuditedCustomer");
    }

    [Fact]
    public async Task Should_load_keys_after_hosted_services_registered_before_encryption()
    {
        var wrapper = new InMemoryKeyWrapper();
        var builder = Host.CreateApplicationBuilder();

        // e.g. migrations: the key table exists when the keys are loaded
        builder.Services.AddHostedService<CreateDatabase>();
        builder.Services
            .AddEncryption(x => x.UseKeyWrapper(_ => wrapper))
            .AddDbContext<AuditedDbContext>(x => x.UseSqlite($"Data Source={_path}").UseEncryption());

        using var host = builder.Build();
        await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));

        wrapper.GenerateCalls.Should().Be(2, "the root key and the blind index key are created on startup");
        await host.StopAsync();
    }

    private sealed class CreateDatabase(IServiceScopeFactory scopeFactory) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AuditedDbContext>().Database.EnsureCreatedAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
        return ValueTask.CompletedTask;
    }

    private sealed class AuditInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            var added = context.ChangeTracker.Entries().Where(x => x.State == EntityState.Added && x.Entity is not AuditEntry).ToList();

            foreach (var entry in added)
                context.Add(new AuditEntry { Payload = $"added {entry.Metadata.ClrType.Name}" });

            return ValueTask.FromResult(result);
        }
    }
}

public sealed class AuditedDbContext(DbContextOptions<AuditedDbContext> options) : DbContext(options)
{
    public DbSet<AuditedCustomer> Customers => Set<AuditedCustomer>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
}

public sealed class AuditedCustomer
{
    public int Id { get; set; }

    [Encrypted, BlindIndex]
    public string? Email { get; set; }
}

public sealed class AuditEntry
{
    public int Id { get; set; }

    [Encrypted]
    public string? Payload { get; set; }
}
