using EntityFrameworkCore.Encryption.ModelBuilderExtensions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.IntegrationTests.Common;

public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Password> Passwords => Set<Password>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<Password>()
            .Property(x => x.EncryptedFluent)
            .IsEncrypted();
    }
}