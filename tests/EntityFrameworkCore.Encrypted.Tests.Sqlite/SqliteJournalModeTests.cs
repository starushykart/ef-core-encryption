using System.Security.Cryptography;
using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Sqlite;

/// <summary>With a rollback journal, SQLite locks the whole file while a table is read.</summary>
public sealed class SqliteJournalModeTests : IAsyncDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"efenc_journal_{Guid.NewGuid():N}.db");
    private readonly byte[] _oldKey = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _newKey = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public async Task Should_re_encrypt_with_rollback_journal()
    {
        await using (var initial = Build(x => x.UseKey(_oldKey)))
        {
            await using var scope = initial.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ProviderDbContext>();
            await context.Database.EnsureCreatedAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE");
            context.AddRange(Enumerable.Range(0, 20).Select(i => new Tag { Group = "g", Name = $"t{i}", Value = $"v{i}" }));
            await context.SaveChangesAsync();
        }

        await using var rotated = Build(x => x.UseKey(_oldKey).UseKey(_newKey, id: 2));

        var result = await rotated.ReEncryptAsync<ProviderDbContext>(batchSize: 5).WaitAsync(TimeSpan.FromSeconds(60));

        result.Should().Be(new ReEncryptionResult(ReEncrypted: 20, Skipped: 0, Invalid: 0));
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_path);
        return ValueTask.CompletedTask;
    }

    private ServiceProvider Build(Action<EncryptionBuilder> configure)
        => new ServiceCollection()
            .AddEncryption(configure)
            .AddDbContext<ProviderDbContext>(x => x.UseSqlite($"Data Source={_path};Default Timeout=5").UseEncryption())
            .BuildServiceProvider();
}
