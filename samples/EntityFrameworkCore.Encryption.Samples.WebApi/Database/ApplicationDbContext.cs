using EntityFrameworkCore.Encryption.Internal.ModelConfigurationExtensions;
using EntityFrameworkCore.Encryption.Samples.WebApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Samples.WebApi.Database;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : EncryptedDbContext(options)
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