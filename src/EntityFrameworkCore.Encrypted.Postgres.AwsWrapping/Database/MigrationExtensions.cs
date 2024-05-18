using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;

public static class MigrationExtensions
{
    public static async Task MigrateEncryptionContextAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<EncryptionMetadataContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
    }
}