using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Storage;
using EntityFrameworkCore.Encrypted.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

public class KeyLifecycleTests(PostgresContainerFixture postgres, ITestOutputHelper helper) : BaseTest(postgres, helper, false)
{
    private static readonly string[] EncryptedColumns =
        [nameof(Password.EncryptedAttribute), nameof(Password.EncryptedBinary), nameof(Password.EncryptedFluent)];

    private readonly InMemoryKeyWrapper _wrapper = new();

    [Fact]
    public async Task Should_count_values_per_column_and_key()
    {
        await AddPasswordsAsync(Provider, 3);
        await Provider.RotateRootKeyAsync<TestDbContext>();
        await AddPasswordsAsync(Provider, 2);

        var usage = await Provider.GetKeyUsageAsync<TestDbContext>();

        usage.Should().BeEquivalentTo(EncryptedColumns.SelectMany(column => new[]
        {
            new KeyUsage("Passwords", column, 1, 0, 3),
            new KeyUsage("Passwords", column, 2, 0, 2)
        }));
    }

    [Fact]
    public async Task Should_re_encrypt_values_with_active_key()
    {
        var originals = await AddPasswordsAsync(Provider, 5);
        await Provider.RotateRootKeyAsync<TestDbContext>();

        var result = await Provider.ReEncryptAsync<TestDbContext>(batchSize: 4);

        result.Should().Be(new ReEncryptionResult(ReEncrypted: 15, Skipped: 0, Invalid: 0));
        (await Provider.GetKeyUsageAsync<TestDbContext>()).Should().OnlyContain(x => x.RootKeyId == 2 && x.Values == 5);
        await AssertReadableAsync(Provider, originals);
    }

    [Fact]
    public async Task Should_not_re_encrypt_values_already_encrypted_with_active_key()
    {
        await AddPasswordsAsync(Provider, 2);
        await Provider.RotateRootKeyAsync<TestDbContext>();
        await Provider.ReEncryptAsync<TestDbContext>();

        var result = await Provider.ReEncryptAsync<TestDbContext>();

        result.Should().Be(new ReEncryptionResult(0, 0, 0));
    }

    [Fact]
    public async Task Should_re_encrypt_with_retrying_execution_strategy()
    {
        var originals = await AddPasswordsAsync(Provider, 2);
        await Provider.RotateRootKeyAsync<TestDbContext>();

        await using var provider = new ServiceCollection()
            .AddEncryption(x => x.UseKeyWrapper(_ => _wrapper))
            .AddDbContext<TestDbContext>(x => x.UseNpgsql(ConnectionString, o => o.EnableRetryOnFailure()).UseEncryption())
            .AddXunitLogging(Helper)
            .BuildServiceProvider(true);

        (await provider.ReEncryptAsync<TestDbContext>()).ReEncrypted.Should().Be(6);
        await AssertReadableAsync(provider, originals);
    }

    [Fact]
    public async Task Should_re_encrypt_values_with_new_data_key_version()
    {
        var originals = await AddPasswordsAsync(Provider, 2);
        await using var upgraded = BuildProvider(x => x.UseDataKeyVersion(3));

        (await upgraded.ReEncryptAsync<TestDbContext>()).ReEncrypted.Should().Be(6);

        (await upgraded.GetKeyUsageAsync<TestDbContext>()).Should().OnlyContain(x => x.RootKeyId == 1 && x.DataKeyVersion == 3);
        await AssertReadableAsync(upgraded, originals);
    }

    [Fact]
    public async Task Should_use_root_key_rotated_by_another_instance()
    {
        await AddPasswordsAsync(Provider, 1);
        await using var other = BuildProvider();
        await other.RotateRootKeyAsync<TestDbContext>();

        await Provider.ReEncryptAsync<TestDbContext>();

        (await Provider.GetKeyUsageAsync<TestDbContext>()).Should().OnlyContain(x => x.RootKeyId == 2);
    }

    [Fact]
    public async Task Should_report_and_keep_values_that_are_not_encrypted()
    {
        var originals = await AddPasswordsAsync(Provider, 2);
        await Provider.RotateRootKeyAsync<TestDbContext>();
        await ExecuteSqlAsync($"""UPDATE "Passwords" SET "EncryptedFluent" = 'plaintext' WHERE "Id" = '{originals[0].Id}'""");

        var usage = await Provider.GetKeyUsageAsync<TestDbContext>();
        var result = await Provider.ReEncryptAsync<TestDbContext>();

        usage.Should().ContainEquivalentOf(new KeyUsage("Passwords", nameof(Password.EncryptedFluent), null, null, 1));
        result.Should().Be(new ReEncryptionResult(ReEncrypted: 5, Skipped: 0, Invalid: 1));
        (await ScalarAsync($"""SELECT "EncryptedFluent" FROM "Passwords" WHERE "Id" = '{originals[0].Id}'""")).Should().Be("plaintext");
    }

    [Fact]
    public async Task Should_skip_null_values()
    {
        var password = Fakers.PasswordFaker.Generate();
        password.EncryptedBinary = null;
        await AddAsync(Provider, password);
        await Provider.RotateRootKeyAsync<TestDbContext>();

        (await Provider.ReEncryptAsync<TestDbContext>()).ReEncrypted.Should().Be(2);
        (await Provider.GetKeyUsageAsync<TestDbContext>()).Select(x => x.Column).Should().NotContain(nameof(Password.EncryptedBinary));
    }

    [Fact]
    public async Task Should_not_overwrite_values_changed_while_re_encrypting()
    {
        var originals = await AddPasswordsAsync(Provider, 3);
        await Provider.RotateRootKeyAsync<TestDbContext>();

        var changed = false;
        await using var provider = BuildProvider(services: x => x.AddLogging(b => b.AddProvider(new CallbackLoggerProvider(message =>
        {
            // after the first batch: the application updates all rows, the scan still holds their previous values
            if (changed || !message.StartsWith("Re-encrypted"))
                return;

            changed = true;
            using var scope = Provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            foreach (var password in context.Passwords)
                password.EncryptedFluent = "changed";
            context.SaveChanges();
        }))));

        var result = await provider.ReEncryptAsync<TestDbContext>(batchSize: 1);

        result.Skipped.Should().BeInRange(2, 3);
        (result.ReEncrypted + result.Skipped).Should().Be(9);
        await using var scope = Provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TestDbContext>().Passwords.Select(x => x.EncryptedFluent).ToListAsync())
            .Should().AllBe("changed").And.HaveCount(originals.Count);
    }

    [Fact]
    public async Task Should_rewrap_root_keys_and_keep_values_readable_without_previous_wrapping_key()
    {
        var originals = await AddPasswordsAsync(Provider, 2);
        await Provider.RotateRootKeyAsync<TestDbContext>();
        originals.AddRange(await AddPasswordsAsync(Provider, 2));

        _wrapper.UseNewMasterKey("new-master-key");
        var rewrapped = await Provider.RewrapRootKeysAsync<TestDbContext>();
        _wrapper.Disable(InMemoryKeyWrapper.WrappingKeyId);

        rewrapped.Should().Be(2);
        (await GetStoredKeysAsync()).Should().OnlyContain(x => x.WrappingKeyId == "new-master-key");

        await using var restarted = BuildProvider();
        await AssertReadableAsync(restarted, originals);
    }

    [Fact]
    public async Task Should_keep_creation_time_of_rewrapped_root_keys()
    {
        await Provider.InitializeEncryptionAsync();
        var before = await GetStoredKeysAsync();

        _wrapper.UseNewMasterKey("new-master-key");
        await Provider.RewrapRootKeysAsync<TestDbContext>();

        (await GetStoredKeysAsync()).Single().CreatedAt.Should().Be(before.Single().CreatedAt);
    }

    [Fact]
    public async Task Should_not_rewrap_static_root_keys()
    {
        await using var provider = new ServiceCollection()
            .AddEncryption(x => x.UseKey(new byte[32]))
            .AddDbContext<TestDbContext>(x => x.UseNpgsql(ConnectionString).UseEncryption())
            .BuildServiceProvider();

        var act = () => provider.RewrapRootKeysAsync<TestDbContext>();

        await act.Should().ThrowAsync<EntityFrameworkEncryptionException>().WithMessage("*nothing to rewrap*");
    }

    protected override void Configure(IServiceCollection services)
        => AddServices(services);

    private ServiceProvider BuildProvider(Action<EncryptionBuilder>? configure = null, Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection();
        AddServices(collection, configure);
        services?.Invoke(collection);
        return collection.AddXunitLogging(Helper).BuildServiceProvider(true);
    }

    private void AddServices(IServiceCollection services, Action<EncryptionBuilder>? configure = null)
        => services
            .AddEncryption(x =>
            {
                x.UseKeyWrapper(_ => _wrapper);
                configure?.Invoke(x);
            })
            .AddDbContext<TestDbContext>(x => x.UseNpgsql(ConnectionString).UseEncryption());

    private static async Task<List<Password>> AddPasswordsAsync(IServiceProvider provider, int count)
    {
        var passwords = Fakers.PasswordFaker.Generate(count);
        await AddAsync(provider, [..passwords]);
        return passwords;
    }

    private static async Task AddAsync(IServiceProvider provider, params Password[] passwords)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        context.AddRange(passwords);
        await context.SaveChangesAsync();
    }

    private static async Task AssertReadableAsync(IServiceProvider provider, IEnumerable<Password> originals)
    {
        await using var scope = provider.CreateAsyncScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<TestDbContext>().Passwords.AsNoTracking().ToDictionaryAsync(x => x.Id);

        foreach (var original in originals)
            original.AssertPasswordEncryption(persisted[original.Id]);
    }

    private async Task<List<EncryptionKeyEntity>> GetStoredKeysAsync()
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Set<EncryptionKeyEntity>().AsNoTracking().ToListAsync();
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var scope = Provider.CreateAsyncScope();
#pragma warning disable EF1002
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.ExecuteSqlRawAsync(sql);
#pragma warning restore EF1002
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var scope = Provider.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
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
