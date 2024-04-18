using EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Database;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Samples.Encryption.Database;

public class MigrationHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var appContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await appContext.Database.MigrateAsync(cancellationToken);

        await scope.ServiceProvider.MigrateEncryptionContext(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}