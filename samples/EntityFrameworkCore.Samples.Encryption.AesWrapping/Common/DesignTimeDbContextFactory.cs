using EntityFrameworkCore.Encryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EntityFrameworkCore.Samples.Encryption.AesWrapping.Database;

internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EncryptedWrappedDbContext>
{
    public EncryptedWrappedDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EncryptedWrappedDbContext>()
            .UseNpgsql()
            .UseDesignTimeEncryption()
            .Options;

        return new EncryptedWrappedDbContext(options);
    }
}