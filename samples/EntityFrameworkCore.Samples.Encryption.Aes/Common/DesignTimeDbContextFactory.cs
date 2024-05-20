using EntityFrameworkCore.Encrypted;
using EntityFrameworkCore.Samples.Encryption.Aes.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EntityFrameworkCore.Samples.Encryption.Aes.Common;

internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EncryptedDbContext>
{
    public EncryptedDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EncryptedDbContext>()
            .UseNpgsql()
            .UseDesignTimeEncryption()
            .Options;

        return new EncryptedDbContext(options);
    }
}