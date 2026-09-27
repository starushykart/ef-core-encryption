using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EncryptionMetadataContext>
{
    public EncryptionMetadataContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EncryptionMetadataContext>()
            .UseNpgsql()
            .Options;

        return new EncryptionMetadataContext(options);
    }
}