using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Database;

public static class MigrationExtensions
{
    public static async Task MigrateEncryptionContext(this IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<EncryptionMetadataContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}