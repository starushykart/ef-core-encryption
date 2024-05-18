using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TestDbContext>
{
    public TestDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql()
            .UseDesignTimeEncryption()
            .Options;

        return new TestDbContext(options);
    }
}