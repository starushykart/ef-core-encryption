using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Providers;

/// <summary>
/// Migrating a database encrypted by other code (or not encrypted) to the library without downtime, on every
/// relational provider. Each test uses its own database.
/// </summary>
public abstract class LegacyMigrationProviderTests(ITestOutputHelper helper) : IAsyncLifetime
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _oldKey = RandomNumberGenerator.GetBytes(32);
    private string _connectionString = null!;

    protected abstract string CreateConnectionString();

    protected abstract void UseProvider(DbContextOptionsBuilder options, string connectionString);

    public async ValueTask InitializeAsync()
    {
        _connectionString = CreateConnectionString();

        await using var provider = Build(legacy: null);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LegacyDbContext>().Database.EnsureCreatedAsync();

        await provider.InitializeEncryptionAsync();
    }

    [Fact]
    public async Task Should_read_legacy_and_new_values_side_by_side()
    {
        var legacy = await SeedLegacyAsync(count: 3);
        await using var provider = Build(new OldDecryptor(_oldKey));
        var added = await AddAsync(provider, new LegacyCustomer { Id = Guid.NewGuid(), Name = "new", Email = "new@example.com", Photo = [9], Status = Level.Premium });

        var all = await ReadAllAsync(provider);

        all.Should().BeEquivalentTo(legacy.Append(added));
    }

    [Fact]
    public async Task Should_read_legacy_values_that_look_like_the_library_format()
    {
        // the first byte of the legacy ciphertext (here: its nonce) is the library's format byte
        var legacy = await SeedLegacyAsync(count: 20, firstByte: 1);
        await using var provider = Build(new OldDecryptor(_oldKey));

        (await ReadAllAsync(provider)).Should().BeEquivalentTo(legacy);
    }

    [Fact]
    public async Task Should_migrate_legacy_values_with_re_encryption()
    {
        var legacy = await SeedLegacyAsync(count: 30);
        await using var migrating = Build(new OldDecryptor(_oldKey));

        (await migrating.GetKeyUsageAsync<LegacyDbContext>()).Should().OnlyContain(x => x.RootKeyId == null);
        await using (var scope = migrating.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<LegacyDbContext>().Customers.AnyAsync(x => x.Email == legacy[0].Email))
                .Should().BeFalse("legacy rows have no blind index until they are migrated");

        var result = await migrating.ReEncryptAsync<LegacyDbContext>(batchSize: 7);

        result.Should().Be(new ReEncryptionResult(ReEncrypted: 30 * 2 + 20, Skipped: 0, Invalid: 0), "photos of every third customer are null");
        (await migrating.GetKeyUsageAsync<LegacyDbContext>()).Should().OnlyContain(x => x.RootKeyId == 1);

        // the legacy decryptor is no longer needed
        await using var migrated = Build(legacy: null);
        await migrated.InitializeEncryptionAsync();
        (await ReadAllAsync(migrated)).Should().BeEquivalentTo(legacy);

        await using var verify = migrated.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<LegacyDbContext>().Customers.SingleAsync(x => x.Email == legacy[5].Email))
            .Name.Should().Be(legacy[5].Name);
    }

    [Fact]
    public async Task Should_migrate_values_encrypted_with_plain_aes_256()
    {
        // AES-256-CBC with a random IV, Base64 of [IV][ciphertext]: also the format of EntityFrameworkCore.Encrypted 1.x
        var customers = Enumerable.Range(0, 40)
            .Select(i => new LegacyCustomer { Id = Guid.NewGuid(), Name = $"customer {i}", Email = $"customer{i}@example.com", Status = (Level)(i % 2) })
            .ToList();

        await using (var plain = CreatePlainContext())
        {
            plain.AddRange(customers.Select(x => new PlainCustomer
            {
                Id = x.Id,
                Name = x.Name,
                Email = Aes256Cbc.Encrypt(x.Email!, _oldKey),
                Status = Aes256Cbc.Encrypt(x.Status.ToString(), _oldKey)
            }));
            await plain.SaveChangesAsync();
        }

        await using var migrating = Build(new Aes256CbcDecryptor(_oldKey));
        var added = await AddAsync(migrating, new LegacyCustomer { Id = Guid.NewGuid(), Name = "new", Email = "new@example.com", Status = Level.Premium });

        (await ReadAllAsync(migrating)).Should().BeEquivalentTo(customers.Append(added));
        (await migrating.ReEncryptAsync<LegacyDbContext>()).Should().Be(new ReEncryptionResult(ReEncrypted: 80, Skipped: 0, Invalid: 0));

        await using var migrated = Build(legacy: null);
        await migrated.InitializeEncryptionAsync();
        (await ReadAllAsync(migrated)).Should().BeEquivalentTo(customers.Append(added));
        (await migrated.GetKeyUsageAsync<LegacyDbContext>()).Should().OnlyContain(x => x.RootKeyId == 1);

        await using var verify = migrated.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<LegacyDbContext>().Customers.SingleAsync(x => x.Email == "customer7@example.com"))
            .Name.Should().Be("customer 7", "the blind index is filled while migrating");
    }

    [Fact]
    public async Task Should_read_values_of_a_newer_data_key_version_before_asking_the_legacy_decryptor()
    {
        // a newer deployment writes with data key version 1 while an older one, still migrating, reads with version 0
        await using var newer = Build(new GarbageDecryptor(), dataKeyVersion: 1);
        var customer = await AddAsync(newer, new LegacyCustomer { Id = Guid.NewGuid(), Name = "new", Email = "new@example.com", Photo = [1, 2], Status = Level.Premium });

        await using var older = Build(new GarbageDecryptor());

        (await ReadAllAsync(older)).Should().ContainSingle().Which.Should().BeEquivalentTo(customer, "the legacy decryptor doesn't authenticate");
    }

    [Fact]
    public async Task Should_read_binary_plaintext_that_starts_like_the_library_format()
    {
        // starts with an Int64 of 1: the format byte, then root key id 0
        byte[] photo = [1, 0, 0, 0, 0, 0, 0, 0, ..Enumerable.Repeat((byte)7, 40)];

        await using (var plain = CreatePlainContext())
        {
            plain.Add(new PlainCustomer { Id = Guid.NewGuid(), Name = "x", Photo = photo, Status = "Basic" });
            await plain.SaveChangesAsync();
        }

        await using var provider = Build(LegacyDecryptor.Plaintext);

        (await ReadAllAsync(provider)).Should().ContainSingle().Which.Photo.Should().Equal(photo);
    }

    [Fact]
    public async Task Should_migrate_plaintext_columns()
    {
        var plain = await SeedLegacyAsync(count: 5, encrypt: false);
        await using var migrating = Build(LegacyDecryptor.Plaintext);

        (await ReadAllAsync(migrating)).Should().BeEquivalentTo(plain);
        await migrating.ReEncryptAsync<LegacyDbContext>();

        await using var migrated = Build(legacy: null);
        await migrated.InitializeEncryptionAsync();
        (await ReadAllAsync(migrated)).Should().BeEquivalentTo(plain);
        (await ReadStoredAsync()).Select(x => x.Email).Should().NotIntersectWith(plain.Select(x => x.Email), "values are encrypted now");
    }

    [Fact]
    public async Task Should_not_pass_tampered_values_to_the_legacy_decryptor()
    {
        await using var provider = Build(LegacyDecryptor.Plaintext);
        await AddAsync(provider, new LegacyCustomer { Id = Guid.NewGuid(), Name = "jane", Email = "jane@example.com", Status = Level.Basic });

        // flip a byte of the ciphertext, keeping the library's header
        await using (var plain = CreatePlainContext())
        {
            var stored = await plain.Customers.SingleAsync();
            var envelope = Convert.FromBase64String(stored.Email!);
            envelope[^1] ^= 1;
            stored.Email = Convert.ToBase64String(envelope);
            await plain.SaveChangesAsync();
        }

        var read = () => ReadAllAsync(provider);

        (await read.Should().ThrowAsync<EntityFrameworkEncryptionException>()).WithMessage("*was modified*");
    }

    [Fact]
    public async Task Should_reject_values_the_legacy_decryptor_does_not_recognize()
    {
        await using (var plain = CreatePlainContext())
        {
            plain.Add(new PlainCustomer { Id = Guid.NewGuid(), Name = "x", Email = "not encrypted", Status = "Basic" });
            await plain.SaveChangesAsync();
        }

        await using var provider = Build(new OldDecryptor(_oldKey));
        var read = () => ReadAllAsync(provider);

        (await read.Should().ThrowAsync<EntityFrameworkEncryptionException>()).WithMessage("*unsupported format*");
    }

    public async ValueTask DisposeAsync()
    {
        await using var provider = Build(legacy: null);
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LegacyDbContext>().Database.EnsureDeletedAsync();

        GC.SuppressFinalize(this);
    }

    private ServiceProvider Build(ILegacyDecryptor? legacy, uint dataKeyVersion = 0)
        => new ServiceCollection()
            .AddEncryption(x =>
            {
                x.UseKey(_key).UseDataKeyVersion(dataKeyVersion);

                if (legacy != null)
                    x.UseLegacyDecryptor(legacy);
            })
            .AddDbContext<LegacyDbContext>(x => UseProvider(x.UseEncryption(), _connectionString))
            .AddXunitLogging(helper)
            .BuildServiceProvider(true);

    private PlainDbContext CreatePlainContext()
    {
        var options = new DbContextOptionsBuilder<PlainDbContext>();
        UseProvider(options, _connectionString);
        return new PlainDbContext(options.Options);
    }

    /// <summary>Rows as the previous code wrote them: with its own encryption, or in plaintext.</summary>
    private async Task<List<LegacyCustomer>> SeedLegacyAsync(int count, bool encrypt = true, byte? firstByte = null)
    {
        var customers = Enumerable.Range(0, count)
            .Select(i => new LegacyCustomer
            {
                Id = Guid.NewGuid(),
                Name = $"customer {i}",
                Email = $"customer{i}@example.com",
                Photo = i % 3 == 0 ? null : [(byte)i, 1, 2],
                Status = i % 2 == 0 ? Level.Basic : Level.Premium
            })
            .ToList();

        await using var plain = CreatePlainContext();

        plain.AddRange(customers.Select(x => new PlainCustomer
        {
            Id = x.Id,
            Name = x.Name,
            Email = encrypt ? OldCrypto.Encrypt(x.Email!, _oldKey, firstByte) : x.Email,
            Photo = x.Photo == null ? null : encrypt ? OldCrypto.Encrypt(x.Photo, _oldKey, firstByte) : x.Photo,
            Status = encrypt ? OldCrypto.Encrypt(x.Status.ToString(), _oldKey, firstByte) : x.Status.ToString()
        }));

        await plain.SaveChangesAsync();
        return customers;
    }

    private static async Task<LegacyCustomer> AddAsync(IServiceProvider provider, LegacyCustomer customer)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LegacyDbContext>();
        db.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }

    private static async Task<List<LegacyCustomer>> ReadAllAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LegacyDbContext>().Customers.AsNoTracking().ToListAsync();
    }

    private async Task<List<PlainCustomer>> ReadStoredAsync()
    {
        await using var plain = CreatePlainContext();
        return await plain.Customers.AsNoTracking().ToListAsync();
    }

    /// <summary>The "previous code": AES-GCM with its own key, stored as <c>[nonce][ciphertext][tag]</c>, Base64 for text.</summary>
    private static class OldCrypto
    {
        public static string Encrypt(string value, byte[] key, byte? firstByte)
            => Convert.ToBase64String(Encrypt(Encoding.UTF8.GetBytes(value), key, firstByte));

        public static byte[] Encrypt(byte[] plaintext, byte[] key, byte? firstByte)
        {
            var result = new byte[12 + plaintext.Length + 16];
            RandomNumberGenerator.Fill(result.AsSpan(0, 12));

            if (firstByte is { } first)
                result[0] = first;

            using var aes = new AesGcm(key, 16);
            aes.Encrypt(result.AsSpan(0, 12), plaintext, result.AsSpan(12, plaintext.Length), result.AsSpan(12 + plaintext.Length));
            return result;
        }

        public static byte[]? Decrypt(byte[] stored, byte[] key)
        {
            if (stored.Length < 28)
                return null;

            var plaintext = new byte[stored.Length - 28];

            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(stored.AsSpan(0, 12), stored.AsSpan(12, plaintext.Length), stored.AsSpan(12 + plaintext.Length), plaintext);
                return plaintext;
            }
            catch (AuthenticationTagMismatchException)
            {
                return null;
            }
        }
    }

    /// <summary>Plain AES-256-CBC with PKCS7 padding, as EntityFrameworkCore.Encrypted 1.x stored strings.</summary>
    private static class Aes256Cbc
    {
        public static string Encrypt(string value, byte[] key)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            return Convert.ToBase64String([..aes.IV, ..aes.EncryptCbc(Encoding.UTF8.GetBytes(value), aes.IV)]);
        }

        public static string? TryDecrypt(string stored, byte[] key)
        {
            var buffer = new byte[stored.Length];

            if (!Convert.TryFromBase64String(stored, buffer, out var length) || length < 32 || length % 16 != 0)
                return null;

            try
            {
                using var aes = Aes.Create();
                aes.Key = key;
                return new UTF8Encoding(false, true).GetString(aes.DecryptCbc(buffer.AsSpan(16, length - 16), buffer.AsSpan(0, 16)));
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                return null;
            }
        }
    }

    private sealed class Aes256CbcDecryptor(byte[] key) : ILegacyDecryptor
    {
        public string? Decrypt(string storedValue, LegacyValueContext context)
            => Aes256Cbc.TryDecrypt(storedValue, key);
    }

    /// <summary>Like an unauthenticated scheme: "decrypts" anything.</summary>
    private sealed class GarbageDecryptor : ILegacyDecryptor
    {
        public string Decrypt(string storedValue, LegacyValueContext context) => "garbage";

        public byte[] Decrypt(byte[] storedValue, LegacyValueContext context) => [0];
    }

    private sealed class OldDecryptor(byte[] key) : ILegacyDecryptor
    {
        public string? Decrypt(string storedValue, LegacyValueContext context)
        {
            var buffer = new byte[storedValue.Length];
            return Convert.TryFromBase64String(storedValue, buffer, out var length)
                   && OldCrypto.Decrypt(buffer[..length], key) is { } plaintext
                ? Encoding.UTF8.GetString(plaintext)
                : null;
        }

        public byte[]? Decrypt(byte[] storedValue, LegacyValueContext context)
            => OldCrypto.Decrypt(storedValue, key);
    }
}

public sealed class LegacyDbContext(DbContextOptions<LegacyDbContext> options) : DbContext(options)
{
    public DbSet<LegacyCustomer> Customers => Set<LegacyCustomer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<LegacyCustomer>(e =>
        {
            e.ToTable("Customers");
            e.Property(x => x.Status).HasConversion<string>().IsEncrypted();
        });
}

/// <summary>The same table without encryption, as the previous code saw it.</summary>
public sealed class PlainDbContext(DbContextOptions<PlainDbContext> options) : DbContext(options)
{
    public DbSet<PlainCustomer> Customers => Set<PlainCustomer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<PlainCustomer>().ToTable("Customers");
}

public sealed record LegacyCustomer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;

    [Encrypted, BlindIndex]
    public string? Email { get; set; }

    [Encrypted]
    public byte[]? Photo { get; set; }

    public Level Status { get; set; }
}

public sealed class PlainCustomer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Email { get; set; }
    public byte[]? Photo { get; set; }
    public string Status { get; set; } = null!;
}

public enum Level { Basic, Premium }
