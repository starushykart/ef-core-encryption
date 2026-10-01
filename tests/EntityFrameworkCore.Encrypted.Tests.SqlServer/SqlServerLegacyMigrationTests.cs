using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.SqlServer;

public sealed class SqlServerLegacyMigrationTests(SqlServerContainerFixture sqlServer, ITestOutputHelper helper) : LegacyMigrationProviderTests(helper)
{
    protected override string CreateConnectionString()
        => new SqlConnectionStringBuilder(sqlServer.ConnectionString) { InitialCatalog = $"legacy_{Guid.NewGuid():N}" }.ConnectionString;

    protected override void UseProvider(DbContextOptionsBuilder options, string connectionString)
        => options.UseSqlServer(connectionString);
}
