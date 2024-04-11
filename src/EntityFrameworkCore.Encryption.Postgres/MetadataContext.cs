using EntityFrameworkCore.Encryption.Metadata;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Postgres;

public class MetadataContext(DbContextOptions<MetadataContext> options) : DbContext(options)
{
    public DbSet<KeyMetadata> Metadata => Set<KeyMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Utils.DefaultEncryptionSchema);
        
        modelBuilder.Entity<KeyMetadata>(entity =>
        {
            entity.ToTable("Metadata");
            entity.HasKey(x => x.ContextId);
        });
    }
}