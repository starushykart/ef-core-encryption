using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Providers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.SqlServer;

public sealed class SqlServerBlindIndexTests(SqlServerContainerFixture sqlServer, ITestOutputHelper helper) : BlindIndexProviderTests(helper)
{
    protected override string CreateConnectionString()
        => new SqlConnectionStringBuilder(sqlServer.ConnectionString) { InitialCatalog = $"bidx_{Guid.NewGuid():N}" }.ConnectionString;

    protected override void UseProvider(DbContextOptionsBuilder options, string connectionString)
        => options.UseSqlServer(connectionString);
}
