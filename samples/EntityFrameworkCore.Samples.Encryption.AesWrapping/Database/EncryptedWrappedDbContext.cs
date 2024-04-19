using EntityFrameworkCore.Encryption.Internal.ModelExtensions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Samples.Encryption.AesWrapping.Database;

public sealed class EncryptedWrappedDbContext(DbContextOptions<EncryptedWrappedDbContext> options) : DbContext(options)
{
    public DbSet<PasswordWithEncryptionWrapping> EncryptedWrappedPasswords => Set<PasswordWithEncryptionWrapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        
        modelBuilder.Entity<PasswordWithEncryptionWrapping>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EncryptedFluent).IsEncrypted();
        });
    }
}