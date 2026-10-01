using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Shared;

public abstract class BaseSharedTest(PostgresContainerFixture postgres, ITestOutputHelper helper): IAsyncLifetime
{
    private NpgsqlConnection _dbConnection = null!;
    private Respawner _respawner = null!;
    protected ServiceProvider Provider { get; private set; } = null!;
    protected string ConnectionString => postgres.ConnectionString;
    protected ITestOutputHelper Helper => helper;
    
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

    public virtual async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();

        Configure(services);

        Provider = services
            .AddXunitLogging(helper)
            .BuildServiceProvider(true);
        
        await MigrateAsync(Provider);
        await InitializeRespawner();
    }

    public async ValueTask DisposeAsync()
    {
        await _respawner.ResetAsync(_dbConnection);
        await _dbConnection.DisposeAsync();
        await Provider.DisposeAsync();
    }

    protected abstract void Configure(IServiceCollection services);
    protected abstract Task MigrateAsync(IServiceProvider provider);
}