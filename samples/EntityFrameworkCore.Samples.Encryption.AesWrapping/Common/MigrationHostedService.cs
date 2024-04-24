using EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Database;
using EntityFrameworkCore.Samples.Encryption.AesWrapping.Database;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Samples.Encryption.AesWrapping.Common;

public class MigrationHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var appContext = scope.ServiceProvider.GetRequiredService<EncryptedWrappedDbContext>();
        await appContext.Database.MigrateAsync(cancellationToken);

        await scope.ServiceProvider.MigrateEncryptionContextAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}