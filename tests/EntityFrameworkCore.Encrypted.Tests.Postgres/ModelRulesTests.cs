using System.Security.Cryptography;
using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

/// <summary>Mappings that can't work with values that encrypt differently every time are rejected when the model is built.</summary>
public class ModelRulesTests
{
    [Fact]
    public void Should_reject_encrypted_key()
        => Build<KeyContext>().Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*Account.Code is part of a key or foreign key*");

    [Fact]
    public void Should_reject_encrypted_foreign_key()
        => Build<ForeignKeyContext>().Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*Line.AccountCode is part of a key or foreign key*");

    [Fact]
    public void Should_reject_encrypted_concurrency_token()
        => Build<ConcurrencyContext>().Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*Account.Version is a concurrency token*");

    [Fact]
    public void Should_reject_unique_index_on_encrypted_column()
        => Build<UniqueIndexContext>().Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*Account.Email is encrypted and can't have a unique index*");

    [Fact]
    public void Should_reject_blind_index_on_json_mapped_type()
        => Build<JsonContext>().Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*blind indexes aren't supported on types mapped to JSON*");

    [Fact]
    public void Should_reject_seed_data_for_encrypted_properties()
        => Build<SeedContext>().Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*has seed data (HasData) for encrypted properties*");

    [Fact]
    public void Should_reject_external_model()
    {
        // a model built elsewhere, e.g. a compiled model
        using var plain = new PlainContext(new DbContextOptionsBuilder<PlainContext>().UseNpgsql("Host=localhost").Options);
        var model = plain.Model;

        using var provider = new ServiceCollection()
            .AddEncryption(x => x.UseKey(RandomNumberGenerator.GetBytes(32)))
            .AddDbContext<PlainContext>(x => x.UseNpgsql("Host=localhost").UseModel(model).UseEncryption())
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlainContext>();

        var query = () => context.Set<Account>().ToQueryString();
        var save = () => context.SaveChanges();

        query.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*can't be combined with UseModel*");
        save.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*can't be combined with UseModel*");
    }

    [Fact]
    public void Should_reject_column_shared_by_encrypted_and_plain_properties()
        => Build<SharedColumnContext>().Should().Throw<EntityFrameworkEncryptionException>()
            .WithMessage("*are both mapped to column Value of Things, but only one of them is encrypted*");

    [Fact]
    public void Should_label_properties_moved_to_another_table_by_that_table()
    {
        using var provider = new ServiceCollection()
            .AddEncryption(x => x.UseKey(RandomNumberGenerator.GetBytes(32)))
            .AddDbContext<SplitContext>(x => x.UseNpgsql("Host=localhost").UseEncryption())
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var entityType = scope.ServiceProvider.GetRequiredService<SplitContext>().Model.FindEntityType(typeof(Account))!;

        string Label(string property)
            => ((EntityFrameworkCore.Encrypted.Common.Plugin.IEncryptionConverter)entityType.FindProperty(property)!.GetValueConverter()!).Encryptor.Label;

        Label(nameof(Account.Code)).Should().Be("Accounts.Code");
        Label(nameof(Account.Email)).Should().Be("AccountDetails.Email");
        Label(nameof(Account.Version)).Should().Be("AccountDetails.Version");
    }

    [Fact]
    public void Should_reject_model_built_by_another_application()
    {
        using var first = BuildWithReplacedFactory();
        using var second = BuildWithReplacedFactory();

        using (var scope = first.CreateScope())
            _ = scope.ServiceProvider.GetRequiredService<PlainContext>().Set<Account>().ToQueryString();

        // the second application would encrypt with the keys of the first one
        using var other = second.CreateScope();
        var context = other.ServiceProvider.GetRequiredService<PlainContext>();
        context.Add(new Account { Code = "a", Email = "jane@example.com" });
        var save = () => context.SaveChanges();

        save.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*was built by another application*");
    }

    private static ServiceProvider BuildWithReplacedFactory()
        => new ServiceCollection()
            .AddEncryption(x => x.UseKey(RandomNumberGenerator.GetBytes(32)))
            .AddDbContext<PlainContext>(x => x.UseNpgsql("Host=localhost").UseEncryption()
                .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, ReplacedCacheKeyFactory>())
            .BuildServiceProvider();

    private static Action Build<TContext>() where TContext : DbContext
        => () =>
        {
            using var provider = new ServiceCollection()
                .AddEncryption(x => x.UseKey(RandomNumberGenerator.GetBytes(32)))
                .AddDbContext<TContext>(x => x.UseNpgsql("Host=localhost").UseEncryption())
                .BuildServiceProvider();
            using var scope = provider.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<TContext>().Model;
        };

    public class KeyContext(DbContextOptions<KeyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Account>(e =>
            {
                e.HasKey(x => x.Code);
                e.Property(x => x.Code).IsEncrypted();
            });
    }

    public class ForeignKeyContext(DbContextOptions<ForeignKeyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>().HasAlternateKey(x => x.Code);
            modelBuilder.Entity<Line>(e =>
            {
                e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountCode).HasPrincipalKey(x => x.Code);
                e.Property(x => x.AccountCode).IsEncrypted();
            });
        }
    }

    public class ConcurrencyContext(DbContextOptions<ConcurrencyContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Account>().Property(x => x.Version).IsConcurrencyToken().IsEncrypted();
    }

    public class UniqueIndexContext(DbContextOptions<UniqueIndexContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Account>(e =>
            {
                e.Property(x => x.Email).IsEncrypted();
                e.HasIndex(x => x.Email).IsUnique();
            });
    }

    public class JsonContext(DbContextOptions<JsonContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Profile>().OwnsOne(x => x.Contact, x => x.ToJson());
    }

    public class SeedContext(DbContextOptions<SeedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Account>(e =>
            {
                e.Property(x => x.Email).IsEncrypted();
                e.HasData(new Account { Id = 1, Code = "a", Email = "seed@example.com" });
            });
    }

    public class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Account>().Property(x => x.Email).IsEncrypted();
    }

    public class SharedColumnContext(DbContextOptions<SharedColumnContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Thing>().ToTable("Things");
            modelBuilder.Entity<PlainThing>().Property(x => x.Value).HasColumnName("Value");
            modelBuilder.Entity<SecretThing>().Property(x => x.Value).HasColumnName("Value").IsEncrypted();
        }
    }

    public class SplitContext(DbContextOptions<SplitContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Account>(e =>
            {
                e.ToTable("Accounts").SplitToTable("AccountDetails", t =>
                {
                    t.Property(x => x.Id);
                    t.Property(x => x.Email);
                    t.Property(x => x.Version);
                });
                e.Property(x => x.Code).IsEncrypted();
                e.Property(x => x.Email).IsEncrypted();
                e.Property(x => x.Version).IsEncrypted();
            });
    }

    public sealed class ReplacedCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), designTime);
    }

    public abstract class Thing
    {
        public int Id { get; set; }
    }

    public class PlainThing : Thing
    {
        public string? Value { get; set; }
    }

    public class SecretThing : Thing
    {
        public string? Value { get; set; }
    }

    public class Account
    {
        public int Id { get; set; }
        public string Code { get; set; } = null!;
        public string? Email { get; set; }
        public string? Version { get; set; }
    }

    public class Profile
    {
        public int Id { get; set; }
        public Contact? Contact { get; set; }
    }

    public class Contact
    {
        [Encrypted, BlindIndex]
        public string? Phone { get; set; }
    }

    public class Line
    {
        public int Id { get; set; }

        public string AccountCode { get; set; } = null!;
    }
}
