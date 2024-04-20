using EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Database;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Samples.Encryption.AesWrapping.Database;

public class MigrationHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var appContext = scope.ServiceProvider.GetRequiredService<EncryptedWrappedDbContext>();
        await appContext.Database.MigrateAsync(cancellationToken);

        await scope.ServiceProvider.MigrateEncryptionContext(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}