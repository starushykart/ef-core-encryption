using Testcontainers.PostgreSql;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;

public class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:latest")
        .WithName($"ef_core_encrypted_postgres_{Guid.NewGuid()}")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public virtual Task InitializeAsync()
        => _container.StartAsync();

    public virtual async Task DisposeAsync()
        => await _container.DisposeAsync();
}