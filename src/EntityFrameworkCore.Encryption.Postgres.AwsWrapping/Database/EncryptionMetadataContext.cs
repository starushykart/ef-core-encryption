using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Database;

internal class EncryptionMetadataContext(DbContextOptions<EncryptionMetadataContext> options) : DbContext(options)
{
    public DbSet<EncryptionMetadata> Metadata => Set<EncryptionMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("encrypt");

        modelBuilder.Entity<EncryptionMetadata>(entity =>
        {
            entity.ToTable("Metadata");
            entity.HasKey(x => x.ContextId);
            entity.Property(x => x.ContextId).HasMaxLength(100);

            entity.Property(x => x.Key).HasConversion(
                x => Convert.ToBase64String(x),
                x => Convert.FromBase64String(x));
        });
    }
}