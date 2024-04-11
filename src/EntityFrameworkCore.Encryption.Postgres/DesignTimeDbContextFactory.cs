using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EntityFrameworkCore.Encryption.Postgres;

internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MetadataContext>
{
    public MetadataContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MetadataContext>()
            .UseNpgsql()
            .Options;

        return new MetadataContext(options);
    }
}