using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Providers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Sqlite;

public sealed class SqliteBlindIndexTests(ITestOutputHelper helper) : BlindIndexProviderTests(helper)
{
    protected override string CreateConnectionString()
        => $"Data Source={Path.Combine(Path.GetTempPath(), $"efenc_{Guid.NewGuid():N}.db")}";

    protected override void UseProvider(DbContextOptionsBuilder options, string connectionString)
        => options.UseSqlite(connectionString);
}
