using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Providers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Sqlite;

/// <summary>Per-command update path: SQLite doesn't support <c>DbBatch</c>; one lock for the whole database.</summary>
public sealed class SqliteProviderTests(ITestOutputHelper helper) : RelationalProviderTests(helper)
{
    protected override string CreateConnectionString()
        => $"Data Source={Path.Combine(Path.GetTempPath(), $"efenc_{Guid.NewGuid():N}.db")}";

    protected override void UseProvider(DbContextOptionsBuilder options, string connectionString)
        => options.UseSqlite(connectionString);
}
