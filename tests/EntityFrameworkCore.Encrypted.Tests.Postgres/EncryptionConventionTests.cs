using System.Security.Cryptography;
using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Plugin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

public class EncryptionConventionTests
{
    [Fact]
    public void Should_encrypt_complex_type_properties()
    {
        var customer = Model<CustomerContext>().FindEntityType(typeof(Customer))!;
        var address = customer.FindComplexProperty(nameof(Customer.Address))!.ComplexType;
        var street = address.FindProperty(nameof(Address.Street))!;
        var city = address.FindProperty(nameof(Address.City))!;
        var zip = address.FindComplexProperty(nameof(Address.Zip))!.ComplexType.FindProperty(nameof(Zip.Code))!;

        RoundTrip(street, "Main st").Should().Be("Main st");
        RoundTrip(city, "Kyiv").Should().Be("Kyiv");
        RoundTrip(zip, "01001").Should().Be("01001");
        ((IEncryptionConverter)street.GetValueConverter()!).Encryptor.Label.Should().Be("Customers.Address_Street");
    }

    [Fact]
    public void Should_encrypt_after_configured_conversion()
    {
        var customer = Model<CustomerContext>().FindEntityType(typeof(Customer))!;

        RoundTrip(customer.FindProperty(nameof(Customer.Status))!, Status.Active).Should().Be(Status.Active);
        RoundTrip(customer.FindProperty(nameof(Customer.Previous))!, Status.Blocked).Should().Be(Status.Blocked);
        RoundTrip(customer.FindProperty(nameof(Customer.Birthday))!, new DateOnly(1990, 5, 17)).Should().Be(new DateOnly(1990, 5, 17));
        RoundTrip(customer.FindProperty(nameof(Customer.Code))!, "abc").Should().Be("ABC", "the configured conversion runs before encryption");
    }

    [Fact]
    public void Should_store_converted_values_encrypted()
    {
        var status = Model<CustomerContext>().FindEntityType(typeof(Customer))!.FindProperty(nameof(Customer.Status))!;

        var stored = (string)status.GetValueConverter()!.ConvertToProvider(Status.Active)!;

        stored.Should().NotBe("Active");
        Convert.FromBase64String(stored)[0].Should().Be(1, "format version of the encrypted value");
    }

    [Fact]
    public void Should_reject_column_type_with_size()
    {
        var act = () => Model<SizedColumnContext>();

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*Customer.Code is encrypted and can't have a maximum length*");
    }

    [Fact]
    public void Should_allow_column_type_without_size()
    {
        var act = () => Model<UnsizedColumnContext>();

        act.Should().NotThrow();
    }

    [Fact]
    public void Should_reject_types_that_are_not_converted_to_string_or_binary()
    {
        var act = () => Model<IntContext>();

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*Customer.Number is Int32*HasConversion*");
    }

    [Fact]
    public void Should_reject_queries_on_complex_type_properties()
    {
        using var provider = BuildProvider<CustomerContext>();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CustomerContext>();

        var act = () => context.Set<Customer>().Where(x => x.Address.Street == "Main st").ToQueryString();

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("Address.Street is encrypted*");
    }

    [Fact]
    public void Should_compare_encrypted_binary_values_by_content()
    {
        var passport = Model<CustomerContext>().FindEntityType(typeof(Customer))!.FindProperty(nameof(Customer.Passport))!;

        var comparer = passport.GetValueComparer();

        comparer.Should().BeOfType<ArrayStructuralComparer<byte>>();
        comparer.Equals(new byte[] { 1, 2 }, new byte[] { 1, 2 }).Should().BeTrue();
        ((byte[])comparer.Snapshot(new byte[] { 1 })!).Should().Equal(1);
    }

    [Fact]
    public void Should_distinguish_occurrences_of_the_same_type_in_queries()
    {
        using var provider = BuildProvider<OrderContext>();
        using var scope = provider.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrderContext>().Set<Order>();

        var billing = () => orders.Where(x => x.Billing.Text == "a").ToQueryString();
        var shipping = () => orders.Where(x => x.Shipping.Text == "a").ToQueryString();
        var ownedEncrypted = () => orders.Where(x => x.Sender.Text == "a").ToQueryString();
        var ownedPlain = () => orders.Where(x => x.Receiver.Text == "a").ToQueryString();

        billing.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("Line.Text is encrypted*");
        shipping.Should().NotThrow();
        ownedEncrypted.Should().Throw<EntityFrameworkEncryptionException>();
        ownedPlain.Should().NotThrow();
    }

    private static object? RoundTrip(IReadOnlyProperty property, object value)
    {
        var converter = property.GetValueConverter();
        converter.Should().BeAssignableTo<IEncryptionConverter>($"{property.Name} must be encrypted");

        return converter!.ConvertFromProvider(converter.ConvertToProvider(value));
    }

    private static IModel Model<TContext>() where TContext : DbContext
    {
        using var provider = BuildProvider<TContext>();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TContext>().Model;
    }

    private static ServiceProvider BuildProvider<TContext>() where TContext : DbContext
        => new ServiceCollection()
            .AddEncryption(x => x.UseKey(RandomNumberGenerator.GetBytes(32)))
            .AddDbContext<TContext>(x => x.UseNpgsql("Host=localhost").UseEncryption())
            .BuildServiceProvider();

    public class CustomerContext(DbContextOptions<CustomerContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Customer>(e =>
            {
                e.ToTable("Customers");
                e.ComplexProperty(x => x.Address, a =>
                {
                    a.Property(x => x.City).IsEncrypted();
                    a.ComplexProperty(x => x.Zip);
                });
                e.Property(x => x.Status).HasConversion<string>().IsEncrypted();
                e.Property(x => x.Previous).HasConversion<string>().IsEncrypted();
                e.Property(x => x.Birthday).HasConversion<string>().IsEncrypted();
                e.Property(x => x.Code).HasConversion(v => v.ToUpperInvariant(), v => v).IsEncrypted();
                e.Ignore(x => x.Number);
            });
    }

    public class OrderContext(DbContextOptions<OrderContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Order>(e =>
            {
                e.ComplexProperty(x => x.Billing, x => x.Property(l => l.Text).IsEncrypted());
                e.ComplexProperty(x => x.Shipping);
                e.OwnsOne(x => x.Sender, x => x.Property(l => l.Text).IsEncrypted());
                e.OwnsOne(x => x.Receiver);
            });
    }

    public class Order
    {
        public int Id { get; set; }
        public Line Billing { get; set; } = new();
        public Line Shipping { get; set; } = new();
        public Line Sender { get; set; } = new();
        public Line Receiver { get; set; } = new();
    }

    public class Line
    {
        public string Text { get; set; } = "";
    }

    public class SizedColumnContext(DbContextOptions<SizedColumnContext> options) : CustomerContextBase(options)
    {
        protected override void Configure(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Customer>().Property(x => x.Code).IsEncrypted().HasColumnType("varchar(20)");
    }

    public class UnsizedColumnContext(DbContextOptions<UnsizedColumnContext> options) : CustomerContextBase(options)
    {
        protected override void Configure(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Customer>().Property(x => x.Code).IsEncrypted().HasColumnType("text");
    }

    public class IntContext(DbContextOptions<IntContext> options) : CustomerContextBase(options)
    {
        protected override void Configure(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Customer>().Property(x => x.Number).IsEncrypted();
    }

    public abstract class CustomerContextBase(DbContextOptions options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Customer>(e =>
            {
                e.ComplexProperty(x => x.Address, a => a.ComplexProperty(x => x.Zip));
                e.Ignore(x => x.Status);
                e.Ignore(x => x.Previous);
                e.Ignore(x => x.Birthday);
                e.Ignore(x => x.Number);
            });
            Configure(modelBuilder);
        }

        protected abstract void Configure(ModelBuilder modelBuilder);
    }

    public class Customer
    {
        public int Id { get; set; }
        public Address Address { get; set; } = new();
        public Status Status { get; set; }
        public Status? Previous { get; set; }
        public DateOnly Birthday { get; set; }
        public string Code { get; set; } = "";
        public int Number { get; set; }

        [Encrypted]
        public byte[]? Passport { get; set; }
    }

    public class Address
    {
        [Encrypted]
        public string Street { get; set; } = "";

        public string City { get; set; } = "";

        public Zip Zip { get; set; } = new();
    }

    public class Zip
    {
        [Encrypted]
        public string Code { get; set; } = "";
    }

    public enum Status { Active, Blocked }
}
