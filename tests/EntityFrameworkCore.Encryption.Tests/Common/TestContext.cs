using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Tests.Common;

public sealed class TestContext : DbContext
{
    public TestContext(DbContextOptions<TestContext> options) : base(options)
        => Database.EnsureCreated();

    public DbSet<User> Users => Set<User>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }
}