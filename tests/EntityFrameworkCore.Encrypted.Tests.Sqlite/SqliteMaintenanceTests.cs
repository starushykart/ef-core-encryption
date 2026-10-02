using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Sqlite;

/// <summary>Progress logs and throttling of re-encryption and blind index rebuilds.</summary>
public sealed class SqliteMaintenanceTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"efenc_maintenance_{Guid.NewGuid():N}.db");
    private readonly byte[] _oldKey = RandomNumberGenerator.GetBytes(32);
    private readonly LogCollector _logs = new();

    [Fact]
    public async Task Should_log_start_progress_and_summary_and_pause_between_batches()
    {
        await using (var initial = Build(x => x.UseKey(_oldKey)))
        {
            await initial.InitializeEncryptionAsync();
            await using var scope = initial.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
            await context.Database.EnsureCreatedAsync();
            context.AddRange(Enumerable.Range(0, 30).Select(i => new Tag { Group = "g", Name = $"t{i}", Value = $"v{i}" }));
            await context.SaveChangesAsync();
        }

        await using var rotated = Build(x => x.UseKey(_oldKey).UseKey(RandomNumberGenerator.GetBytes(32), id: 2));
        _logs.Messages.Clear();
        var watch = Stopwatch.StartNew();

        var result = await rotated.ReEncryptAsync<ProviderDbContext>(new MaintenanceOptions { BatchSize = 10, BatchDelay = TimeSpan.FromMilliseconds(200) });

        result.Should().Be(new ReEncryptionResult(ReEncrypted: 30, Skipped: 0, Invalid: 0));
        watch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(600), "three batches, each followed by a pause");
        _logs.Messages.Should().Contain(x => x.StartsWith("Re-encrypting values of ProviderDbContext with root key 2, data key version 0, 10 values per batch"));
        _logs.Messages.Should().Contain(x => x.Contains("Tags: 30 rows with encrypted values"));
        _logs.Messages.Should().Contain(x => x.Contains("Tags finished in") && x.Contains("30 rows scanned, 30 updated"));
        _logs.Messages.Should().Contain(x => x.StartsWith("Re-encryption of ProviderDbContext finished in") && x.Contains("30 values re-encrypted"));
    }

    [Fact]
    public async Task Should_reject_invalid_options()
    {
        await using var provider = Build(x => x.UseKey(_oldKey));

        var batch = () => provider.ReEncryptAsync<ProviderDbContext>(new MaintenanceOptions { BatchSize = 0 });
        var delay = () => provider.RebuildBlindIndexesAsync<ProviderDbContext>(new MaintenanceOptions { BatchDelay = TimeSpan.FromSeconds(-1) });

        await batch.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await delay.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
        return ValueTask.CompletedTask;
    }

    private ServiceProvider Build(Action<EncryptionBuilder> configure)
        => new ServiceCollection()
            .AddLogging(x => x.AddProvider(_logs))
            .AddEncryption(configure)
            .AddDbContext<ProviderDbContext>(x => x.UseSqlite($"Data Source={_path}").UseEncryption())
            .BuildServiceProvider();

    private sealed class LogCollector : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Messages.Enqueue(formatter(state, exception));
        }

        public void Dispose()
        {
        }
    }
}
