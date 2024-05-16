using EntityFrameworkCore.Encrypted.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;

namespace EntityFrameworkCore.Encrypted.IntegrationTests.Common;

public abstract class BaseDatabaseTest(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private NpgsqlConnection _dbConnection = null!;
    private Respawner _respawner = null!;
    protected ServiceProvider Provider { get; private set; } = null!;
    protected string ConnectionString => postgres.ConnectionString;
    
    private async Task InitializeRespawner()
    {
        _dbConnection = new NpgsqlConnection(postgres.ConnectionString);
        await _dbConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = ["__EFMigrationsHistory"]
        });
    }

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        
        Configure(services);

        Provider = services.BuildServiceProvider(true);

        await using var scope = Provider.CreateAsyncScope();
        await MigrateAsync(scope);
        await InitializeRespawner();
    }

    public async Task DisposeAsync()
    {
        await _respawner.ResetAsync(_dbConnection);
        await _dbConnection.DisposeAsync();
        await Provider.DisposeAsync();
    }

    protected abstract void Configure(IServiceCollection services);
    protected abstract Task MigrateAsync(IServiceScope scope);
}