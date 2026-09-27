using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Storage;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

public class EncryptionFormatTests : IDisposable
{
    private readonly ServiceProvider _provider = DocumentDbContext.BuildProvider(x => x.UseKey(RandomNumberGenerator.GetBytes(32)));

    [Theory]
    [InlineData("secret")]
    [InlineData("")]
    [InlineData("юнікод ✓ 🔐")]
    public void Should_encrypt_and_decrypt_strings(string value)
    {
        var converter = DocumentDbContext.GetConverter(_provider, nameof(Document.Text));

        var encrypted = (string)converter.ConvertToProvider(value)!;

        encrypted.Should().NotBe(value);
        converter.ConvertFromProvider(encrypted).Should().Be(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    public void Should_encrypt_and_decrypt_binary(int size)
    {
        var converter = DocumentDbContext.GetConverter(_provider, nameof(Document.Blob));
        var value = RandomNumberGenerator.GetBytes(size);

        var encrypted = (byte[])converter.ConvertToProvider(value)!;

        encrypted.Length.Should().Be(size + 35);
        ((byte[])converter.ConvertFromProvider(encrypted)!).Should().Equal(value);
    }

    [Fact]
    public void Should_use_random_nonce_per_value()
    {
        var converter = DocumentDbContext.GetConverter(_provider, nameof(Document.Text));

        converter.ConvertToProvider("secret").Should().NotBe(converter.ConvertToProvider("secret"));
    }

    [Fact]
    public void Should_write_format_and_key_id_header()
    {
        var converter = DocumentDbContext.GetConverter(_provider, nameof(Document.Blob));

        var encrypted = (byte[])converter.ConvertToProvider(new byte[] { 1, 2, 3 })!;

        encrypted[0].Should().Be(1, "format version");
        BinaryPrimitives.ReadUInt16BigEndian(encrypted.AsSpan(1, 2)).Should().Be(1, "root key id");
    }

    [Fact]
    public void Should_detect_tampering()
    {
        var converter = DocumentDbContext.GetConverter(_provider, nameof(Document.Blob));
        var encrypted = (byte[])converter.ConvertToProvider(Encoding.UTF8.GetBytes("secret"))!;

        encrypted[^20] ^= 1;
        var act = () => converter.ConvertFromProvider(encrypted);

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*modified or encrypted for another column*");
    }

    [Fact]
    public void Should_bind_value_to_its_column()
    {
        var text = DocumentDbContext.GetConverter(_provider, nameof(Document.Text));
        var other = DocumentDbContext.GetConverter(_provider, nameof(Document.Other));

        var act = () => other.ConvertFromProvider(text.ConvertToProvider("secret"));

        act.Should().Throw<EntityFrameworkEncryptionException>();
    }

    [Fact]
    public void Should_allow_moving_values_between_columns_with_same_label()
    {
        var sharedA = DocumentDbContext.GetConverter(_provider, nameof(Document.SharedA));
        var sharedB = DocumentDbContext.GetConverter(_provider, nameof(Document.SharedB));

        sharedB.ConvertFromProvider(sharedA.ConvertToProvider("secret")).Should().Be("secret");
    }

    [Fact]
    public void Should_label_values_with_table_and_column_by_default()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();

        context.GetLabel(nameof(Document.Text)).Should().Be("Documents.Text");
        context.GetLabel(nameof(Document.RenamedColumn)).Should().Be("Documents.OldName");
        context.GetLabel(nameof(Document.SharedA)).Should().Be("shared");
    }

    [Fact]
    public void Should_reject_value_with_unknown_format()
    {
        var converter = DocumentDbContext.GetConverter(_provider, nameof(Document.Blob));

        var act = () => converter.ConvertFromProvider(new byte[40]);

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*unsupported format*");
    }

    [Fact]
    public void Should_add_key_table_to_model_with_and_without_DI()
    {
        using var scope = _provider.CreateScope();
        var withDi = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();

        using var withoutDi = new DocumentDbContext(new DbContextOptionsBuilder<DocumentDbContext>()
            .UseNpgsql(DocumentDbContext.ConnectionString)
            .UseEncryption()
            .Options);

        foreach (var context in new DbContext[] { withDi, withoutDi })
        {
            var entity = context.Model.FindEntityType(typeof(EncryptionKeyEntity));

            entity.Should().NotBeNull();
            entity!.GetTableName().Should().Be(EncryptionKeyEntity.TableName);
            entity.FindPrimaryKey()!.Properties.Single().ValueGenerated.Should().Be(ValueGenerated.Never);
        }
    }

    [Fact]
    public void Should_reject_max_length_on_encrypted_property()
    {
        var options = new DbContextOptionsBuilder<MaxLengthDbContext>()
            .UseNpgsql(DocumentDbContext.ConnectionString)
            .UseEncryption()
            .Options;
        using var context = new MaxLengthDbContext(options);

        var act = () => context.Model;

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*Document.Text*maximum length*");
    }

    public void Dispose()
        => _provider.Dispose();

    public sealed class MaxLengthDbContext(DbContextOptions<MaxLengthDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Document>().Property(x => x.Text).IsEncrypted().HasMaxLength(100);
    }
}
