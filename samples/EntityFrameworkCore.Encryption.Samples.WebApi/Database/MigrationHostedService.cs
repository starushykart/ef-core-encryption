using EntityFrameworkCore.Encryption.Postgres;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Samples.WebApi.Database;

public class MigrationHostedService(IDbContextFactory<MetadataContext> metadataContextFactory, IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var appContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await appContext.Database.MigrateAsync(cancellationToken);
        
        await using var context = await metadataContextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}