using System.Security.Cryptography;
using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

/// <summary>Blind index model and query rules; nothing is sent to the database.</summary>
public sealed class BlindIndexModelTests : IDisposable
{
    private readonly ServiceProvider _provider = new ServiceCollection()
        .AddEncryption(x => x.UseRootKeyProvider(_ => new FixedKeyProvider()))
        .AddDbContext<AccountContext>(x => x.UseNpgsql("Host=localhost").UseEncryption())
        .BuildServiceProvider();

    private readonly IServiceScope _scope;
    private readonly AccountContext _context;

    public BlindIndexModelTests()
    {
        _scope = _provider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<AccountContext>();
    }

    [Fact]
    public void Should_add_nullable_indexed_shadow_column()
    {
        var account = _context.Model.FindEntityType(typeof(Account))!;
        var index = account.FindProperty("Email_Index")!;

        index.IsShadowProperty().Should().BeTrue();
        index.IsNullable.Should().BeTrue("existing rows get the column without a value");
        index.GetMaxLength().Should().Be(32);
        index.GetValueConverter()!.ProviderClrType.Should().Be<byte[]>();
        index.GetColumnType().Should().Be("bytea");
        account.GetIndexes().Should().Contain(x => x.Properties.Single() == index);
        account.FindProperty("Login_Index").Should().BeNull("Login has no blind index");
        account.FindProperty("Level_Index")!.IsNullable.Should().BeTrue("also for properties of non-nullable value types");
    }

    [Fact]
    public void Should_hash_normalized_values_per_column()
    {
        var account = _context.Model.FindEntityType(typeof(Account))!;

        var email = Hash(account, "Email_Index", " Jane@Example.com");
        var phone = Hash(account, "Phone_Index", "jane@example.com");

        email.Should().HaveCount(32).And.Equal(Hash(account, "Email_Index", "jane@example.com"));
        phone.Should().NotEqual(email, "each column has its own key");
        Hash(account, "Phone_Index", "Jane@example.com").Should().NotEqual(phone, "phone isn't normalized");
    }

    [Fact]
    public void Should_keep_normalization_out_of_the_design_time_model()
    {
        // migration snapshots are generated from the design-time model and can't contain delegates
        var model = _context.GetService<IDesignTimeModel>().Model;
        var email = model.FindEntityType(typeof(Account))!.FindProperty(nameof(Account.Email))!;

        email.GetAnnotations().Should().NotContain(x => x.Value is Delegate);
        email.FindAnnotation("Microsoft.EntityFrameworkCore.Encrypted.BlindIndex")!.Value.Should().Be(true);
    }

    [Fact]
    public void Should_compare_blind_index_in_queries()
    {
        var email = "jane@example.com";

        var sql = _context.Accounts.Where(x => x.Email == email).ToQueryString();

        sql.Should().Contain("\"Email_Index\" = @email");
    }

    [Fact]
    public void Should_reject_other_queries_on_blind_indexed_columns()
    {
        var name = "jane";
        Func<IQueryable>[] queries =
        [
            () => _context.Accounts.Where(x => x.Email!.StartsWith(name)),
            () => _context.Accounts.OrderBy(x => x.Email),
            () => _context.Accounts.Where(x => x.Email == x.Login),
            () => _context.Accounts.Where(x => x.Email == x.Phone)
        ];

        foreach (var query in queries)
            query.Invoking(x => x().ToQueryString()).Should().Throw<EntityFrameworkEncryptionException>();
    }

    [Fact]
    public void Should_reject_blind_index_without_encryption()
        => Invoking<NotEncryptedContext>().Should().Throw<EntityFrameworkEncryptionException>()
            .WithMessage("*Account.Login has a blind index but isn't encrypted*");

    [Fact]
    public void Should_reject_normalization_of_binary_values()
        => Invoking<BinaryNormalizationContext>().Should().Throw<EntityFrameworkEncryptionException>()
            .WithMessage("*normalization is only supported for values stored as strings*");

    [Fact]
    public void Should_reject_blind_index_on_complex_type_property()
        => Invoking<ComplexContext>().Should().Throw<EntityFrameworkEncryptionException>()
            .WithMessage("*blind indexes aren't supported on properties of complex types*");

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    private static byte[] Hash(IReadOnlyEntityType type, string property, string value)
        => (byte[])type.FindProperty(property)!.GetValueConverter()!.ConvertToProvider(value)!;

    private static Action Invoking<TContext>() where TContext : DbContext
        => () =>
        {
            using var provider = new ServiceCollection()
                .AddEncryption(x => x.UseKey(RandomNumberGenerator.GetBytes(32)))
                .AddDbContext<TContext>(x => x.UseNpgsql("Host=localhost").UseEncryption())
                .BuildServiceProvider();
            using var scope = provider.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<TContext>().Model;
        };

    private sealed class FixedKeyProvider : IRootKeyProvider
    {
        private readonly byte[] _rootKey = RandomNumberGenerator.GetBytes(32);
        private readonly byte[] _indexKey = RandomNumberGenerator.GetBytes(32);

        public Task<RootKey> GetActiveRootKeyAsync(Type dbContextType, CancellationToken cancellationToken) => Task.FromResult(new RootKey(1, _rootKey));
        public Task<RootKey?> GetRootKeyAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken) => Task.FromResult<RootKey?>(new RootKey(1, _rootKey));
        public Task<int?> GetActiveRootKeyIdAsync(Type dbContextType, CancellationToken cancellationToken) => Task.FromResult<int?>(1);
        public Task<RootKey> RotateRootKeyAsync(Type dbContextType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RewrapRootKeysAsync(Type dbContextType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<byte[]> GetIndexKeyAsync(Type dbContextType, CancellationToken cancellationToken) => Task.FromResult(_indexKey);
    }

    public sealed class AccountContext(DbContextOptions<AccountContext> options) : DbContext(options)
    {
        public DbSet<Account> Accounts => Set<Account>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Account>(e =>
            {
                e.Property(x => x.Email).IsEncrypted().HasBlindIndex(x => x.Trim().ToLowerInvariant());
                e.Property(x => x.Level).HasConversion<string>().IsEncrypted().HasBlindIndex();
            });
    }

    public sealed class NotEncryptedContext(DbContextOptions<NotEncryptedContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Account>().Property(x => x.Login).HasBlindIndex();
    }

    public sealed class BinaryNormalizationContext(DbContextOptions<BinaryNormalizationContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Account>().Property(x => x.Photo).IsEncrypted().HasBlindIndex(x => x.ToUpperInvariant());
    }

    public sealed class ComplexContext(DbContextOptions<ComplexContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Holder>().ComplexProperty(x => x.Contact);
    }

    public sealed class Account
    {
        public int Id { get; set; }
        public string? Email { get; set; }
        public string? Login { get; set; }

        [Encrypted, BlindIndex]
        public string? Phone { get; set; }

        public byte[]? Photo { get; set; }

        public Level Level { get; set; }
    }

    public enum Level { Basic, Premium }

    public sealed class Holder
    {
        public int Id { get; set; }
        public Contact Contact { get; set; } = new();
    }

    public sealed class Contact
    {
        [Encrypted, BlindIndex]
        public string? Email { get; set; }
    }
}
