using EntityFrameworkCore.Encrypted.Annotations;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Samples.Encryption.Aes.Database;

public sealed class EncryptedDbContext(DbContextOptions<EncryptedDbContext> options) : DbContext(options)
{
    public DbSet<PasswordWithEncryption> EncryptedPasswords => Set<PasswordWithEncryption>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        
        modelBuilder.Entity<PasswordWithEncryption>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EncryptedFluent).IsEncrypted();
        });
    }
}