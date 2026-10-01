using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Providers;
using EntityFrameworkCore.Encrypted.Tests.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using Xunit;

[assembly: AssemblyFixture(typeof(SqlServerContainerFixture))]

namespace EntityFrameworkCore.Encrypted.Tests.SqlServer;

/// <summary>Locking reads (READ COMMITTED without row versioning): re-encryption streams a table while updating it.</summary>
public sealed class SqlServerProviderTests(SqlServerContainerFixture sqlServer, ITestOutputHelper helper) : RelationalProviderTests(helper)
{
    protected override string CreateConnectionString()
        => new SqlConnectionStringBuilder(sqlServer.ConnectionString) { InitialCatalog = $"provider_{Guid.NewGuid():N}" }.ConnectionString;

    protected override void UseProvider(DbContextOptionsBuilder options, string connectionString)
        => options.UseSqlServer(connectionString);
}

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithName($"ef_core_encrypted_mssql_{Guid.NewGuid()}")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
        => await _container.StartAsync();

    public async ValueTask DisposeAsync()
        => await _container.DisposeAsync();
}
