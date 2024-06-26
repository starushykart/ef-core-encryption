using System.ComponentModel.DataAnnotations;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Plugin;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

public class DbContextEncryptionConfigurationTests
{
    [Fact]
    public void Fluent_property_configuration_should_add_annotation()
    {
        using var context = new TestsUnitTestContext();

        var entity = context.Model.FindEntityType(typeof(TestEntity));

        var encryptedFluentProperty = entity?.GetProperty(nameof(TestEntity.EncryptedFluent));
        var encryptedAnnotation = encryptedFluentProperty?.FindAnnotation(PropertyAnnotations.IsEncrypted);

        encryptedFluentProperty.Should().NotBeNull();
        encryptedAnnotation.Should().NotBeNull();
        encryptedAnnotation?.Value.As<bool>().Should().BeTrue();
    }
    
    [Fact]
    public void Fluent_and_attribute_configuration_should_add_value_converter()
    {
        using var context = new TestsUnitTestContext();

        var entity = context.Model.FindEntityType(typeof(TestEntity));

        var encryptedFluentProperty = entity?.GetProperty(nameof(TestEntity.EncryptedFluent));
        var encryptedAttributeProperty = entity?.GetProperty(nameof(TestEntity.EncryptedAttribute));

        var encryptedFluentPropertyValueConverter = encryptedFluentProperty?.GetValueConverter();
        var encryptedAttributePropertyValueConverter = encryptedAttributeProperty?.GetValueConverter();

        encryptedFluentProperty.Should().NotBeNull();
        encryptedAttributeProperty.Should().NotBeNull();
        
        encryptedFluentPropertyValueConverter
            .Should().NotBeNull()
            .And.BeOfType<EncryptionConverter>();
        
        encryptedAttributePropertyValueConverter
            .Should().NotBeNull()
            .And.BeOfType<EncryptionConverter>();
    }

    [Fact]
    public void Should_throw_if_encryption_applied_to_non_string_property()
    {
        var act = () =>
        {
            using var context = new TestsUnitTestContext(applyEncryptionToNonStringProp: true);
            context.TestEntities.Add(new TestEntity());
        };

        act.Should().ThrowExactly<EntityFrameworkEncryptionException>();
    }
    
    [Fact]
    public void Should_add_encryption_plugin()
    {
        using var context = new TestsUnitTestContext();

        var encryptionPlugin = context.GetService<IConventionSetPlugin>();

        encryptionPlugin.Should().NotBeNull();
        encryptionPlugin.Should().BeOfType<EncryptionConventionPlugin>();
    }
    
    public class TestsUnitTestContext(bool applyEncryptionToNonStringProp = false) : DbContext
    {
        public DbSet<TestEntity> TestEntities => Set<TestEntity>();
    
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseNpgsql().UseAes256Encryption(TestUtils.GenerateAesKeyBase64());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>(entity =>
            {
                entity.Property(x => x.EncryptedFluent).IsEncrypted();

                if (applyEncryptionToNonStringProp)
                    entity.Property(x => x.NonStringProperty).IsEncrypted();
            });
        }
    }
    
    public class TestEntity
    {
        public Guid Id { get; set; }

        [Encrypted]
        [MaxLength(200)]
        public string EncryptedAttribute { get; set; } = null!;
    
        [MaxLength(200)]
        public string EncryptedFluent { get; set; } = null!;
    
        public int NonStringProperty { get; set; }
    }
}