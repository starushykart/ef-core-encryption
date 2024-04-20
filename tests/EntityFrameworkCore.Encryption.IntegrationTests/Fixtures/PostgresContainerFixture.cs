using Testcontainers.PostgreSql;

namespace EntityFrameworkCore.Encryption.IntegrationTests.Fixtures;

public class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:latest")
        .WithName($"db-encryption_postgres_{Guid.NewGuid()}")
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public virtual Task InitializeAsync()
        => _container.StartAsync();

    public virtual async Task DisposeAsync()
        => await _container.DisposeAsync();
}