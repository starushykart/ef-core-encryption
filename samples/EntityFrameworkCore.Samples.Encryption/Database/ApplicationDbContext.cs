using EntityFrameworkCore.Encryption.Internal.ModelConfigurationExtensions;
using EntityFrameworkCore.Samples.Encryption.Models;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Samples.Encryption.Database;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Password> Passwords => Set<Password>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        
        modelBuilder.Entity<Password>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EncryptedFluent).IsEncrypted();
        });
    }
}