using EntityFrameworkCore.Samples.Encryption.Aes.Database;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Samples.Encryption.Aes.Common;

public class MigrationHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var appContext = scope.ServiceProvider.GetRequiredService<EncryptedDbContext>();
        await appContext.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}