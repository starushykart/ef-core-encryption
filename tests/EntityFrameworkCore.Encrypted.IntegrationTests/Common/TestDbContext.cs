using EntityFrameworkCore.Encrypted.Annotations;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encrypted.IntegrationTests.Common;

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