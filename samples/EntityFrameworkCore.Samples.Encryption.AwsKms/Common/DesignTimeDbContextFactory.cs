using EntityFrameworkCore.Encrypted;
using EntityFrameworkCore.Samples.Encryption.AwsKms.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EntityFrameworkCore.Samples.Encryption.AwsKms.Common;

internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EncryptedDbContext>
{
    public EncryptedDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EncryptedDbContext>()
            .UseNpgsql()
            .UseEncryption()
            .Options;

        return new EncryptedDbContext(options);
    }
}