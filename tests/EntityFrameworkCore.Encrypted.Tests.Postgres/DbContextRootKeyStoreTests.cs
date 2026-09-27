using EntityFrameworkCore.Encrypted.Common.Storage;
using EntityFrameworkCore.Encrypted.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

public class DbContextRootKeyStoreTests(PostgresContainerFixture postgres, ITestOutputHelper helper) : BaseTest(postgres, helper, false)
{
    private readonly InMemoryKeyWrapper _wrapper = new();

    [Fact]
    public async Task Should_store_wrapped_root_key_in_context_database()
    {
        await Provider.InitializeEncryptionAsync();
        await using var scope = Provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var stored = await context.Set<EncryptionKeyEntity>().SingleAsync();

        stored.Id.Should().Be(1);
        stored.WrappingKeyId.Should().Be(InMemoryKeyWrapper.WrappingKeyId);
        stored.WrappedKey.Should().NotBeEmpty();
        _wrapper.GenerateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Should_encrypt_and_decrypt_with_stored_root_key_after_restart()
    {
        var original = Fakers.PasswordFaker.Generate();

        await using (var scope = Provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            context.Add(original);
            await context.SaveChangesAsync();
        }

        await using var restarted = BuildProvider(ConnectionString);
        await using var restartedScope = restarted.CreateAsyncScope();

        var persisted = await restartedScope.ServiceProvider.GetRequiredService<TestDbContext>().Passwords
            .SingleAsync(x => x.Id == original.Id);

        original.AssertPasswordEncryption(persisted);
        _wrapper.GenerateCalls.Should().Be(1);
        _wrapper.UnwrapCalls.Should().Be(1);
    }

    [Fact]
    public async Task Should_create_single_root_key_when_instances_start_concurrently()
    {
        var instances = Enumerable.Range(0, 5).Select(_ => BuildProvider(ConnectionString)).ToList();

        await Task.WhenAll(instances.Select(x => x.InitializeEncryptionAsync()));

        await using var scope = Provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TestDbContext>().Set<EncryptionKeyEntity>().CountAsync()).Should().Be(1);

        foreach (var instance in instances)
            await instance.DisposeAsync();
    }

    [Fact]
    public async Task Should_load_keys_on_first_use_when_database_is_migrated_after_start()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = $"not_migrated_{Guid.NewGuid():N}" }.ConnectionString;
        await using var provider = BuildProvider(connectionString);

        await provider.InitializeEncryptionAsync();
        await provider.MigrateContextAsync<TestDbContext>(useFactory: false);

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        var original = Fakers.PasswordFaker.Generate();
        context.Add(original);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        original.AssertPasswordEncryption(await context.Passwords.SingleAsync(x => x.Id == original.Id));

        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task TryAdd_should_report_existing_root_key()
    {
        var store = new DbContextRootKeyStore(Provider.GetRequiredService<IServiceScopeFactory>());
        var rootKey = new WrappedRootKey(1, "wrapping-key", [1, 2, 3], DateTimeOffset.UtcNow);

        (await store.TryAddAsync(typeof(TestDbContext), rootKey, CancellationToken.None)).Should().BeTrue();
        (await store.TryAddAsync(typeof(TestDbContext), rootKey, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task TryAdd_should_rethrow_failures_other_than_existing_root_key()
    {
        var store = new DbContextRootKeyStore(Provider.GetRequiredService<IServiceScopeFactory>());
        var invalid = new WrappedRootKey(1, new string('x', 5000), [1, 2, 3], DateTimeOffset.UtcNow);

        var act = () => store.TryAddAsync(typeof(TestDbContext), invalid, CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    protected override void Configure(IServiceCollection services)
        => AddServices(services, ConnectionString);

    private ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        AddServices(services, connectionString);
        return services.AddXunitLogging(Helper).BuildServiceProvider(true);
    }

    private void AddServices(IServiceCollection services, string connectionString)
        => services
            .AddEncryption(x => x.UseKeyWrapper(_ => _wrapper))
            .AddDbContext<TestDbContext>(x => x.UseNpgsql(connectionString).UseEncryption());
}
