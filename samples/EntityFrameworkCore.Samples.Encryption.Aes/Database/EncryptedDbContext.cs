using EntityFrameworkCore.Encrypted.Annotations;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Samples.Encryption.Aes.Database;

public sealed class EncryptedDbContext(DbContextOptions<EncryptedDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Contract> Contracts => Set<Contract>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(e =>
        {
            e.Property(x => x.Phone).IsEncrypted();

            // searchable by equality, regardless of case and surrounding spaces
            e.Property(x => x.Email).HasBlindIndex(v => v.Trim().ToLowerInvariant());
            e.Property(x => x.Status).HasConversion<string>().IsEncrypted();
            e.Property(x => x.BirthDate).HasConversion<string>().IsEncrypted();
            e.ComplexProperty(x => x.Address);
            e.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<Contract>().HasIndex(x => x.Number).IsUnique();
    }
}
