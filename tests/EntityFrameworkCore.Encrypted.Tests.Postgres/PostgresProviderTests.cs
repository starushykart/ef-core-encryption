using EntityFrameworkCore.Encrypted.Tests.Postgres.Common;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Providers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

[Collection(nameof(IntegrationTestsCollection))]
public sealed class PostgresProviderTests(PostgresContainerFixture postgres, ITestOutputHelper helper) : RelationalProviderTests(helper)
{
    protected override string CreateConnectionString()
        => new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = $"provider_{Guid.NewGuid():N}" }.ConnectionString;

    protected override void UseProvider(DbContextOptionsBuilder options, string connectionString)
        => options.UseNpgsql(connectionString);
}
