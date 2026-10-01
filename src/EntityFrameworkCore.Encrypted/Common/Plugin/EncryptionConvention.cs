using System.Reflection;
using System.Text.RegularExpressions;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Crypto;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Common.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

internal sealed partial class EncryptionConvention(DataKeyRing? keyRing, Type contextType, IValueConverterSelector converterSelector)
    : IModelInitializedConvention, IModelFinalizingConvention
{
    // the key table is part of every encrypted model, with or without DI, so migrations always match the runtime model;
    // data annotation level: convention level entity types unreachable by navigations are removed by ModelCleanupConvention
    public void ProcessModelInitialized(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var entity = modelBuilder.Entity(typeof(EncryptionKeyEntity), fromDataAnnotation: true)!;

        entity.ToTable(EncryptionKeyEntity.TableName, fromDataAnnotation: true);
        entity.PrimaryKey([nameof(EncryptionKeyEntity.Id)], fromDataAnnotation: true);
        entity.Property(typeof(int), nameof(EncryptionKeyEntity.Id), fromDataAnnotation: true)!
            .ValueGenerated(ValueGenerated.Never, fromDataAnnotation: true);
        entity.Property(typeof(string), nameof(EncryptionKeyEntity.WrappingKeyId), fromDataAnnotation: true)!
            .HasMaxLength(2048, fromDataAnnotation: true);
    }

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        // the migrations history table model is built with the context conventions as well
        if (modelBuilder.Metadata.FindEntityType(typeof(HistoryRow)) != null)
        {
            modelBuilder.HasNoEntityType(modelBuilder.Metadata.FindEntityType(typeof(EncryptionKeyEntity))!, fromDataAnnotation: true);
            return;
        }

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (var property in GetDeclaredProperties(entityType).Where(IsEncrypted))
                Encrypt(property);
        }
    }

    private void Encrypt(IConventionProperty property)
    {
        var name = DisplayName(property);

        // the limit would apply to the ciphertext (plaintext + 35 bytes, Base64), not to the value
        if (property.GetMaxLength() != null || property.GetColumnType() is { } columnType && SizedColumnType().IsMatch(columnType))
            throw new EntityFrameworkEncryptionException(
                $"{name} is encrypted and can't have a maximum length: it would limit the stored ciphertext, not the value. " +
                "Remove MaxLength/HasMaxLength or the size from HasColumnType");

        // a configured conversion runs first and its string or byte[] result is encrypted: enums, dates, value objects
        var conversion = property.GetValueConverter() ?? GetProviderTypeConversion(property);
        var encryptor = new FieldEncryptor(keyRing, contextType, GetLabel(property));

        var converter = EncryptionConverters.Create(encryptor, conversion, property.ClrType)
            ?? throw new EntityFrameworkEncryptionException(
                $"{name} is {property.ClrType.Name}: only string and byte[] values can be encrypted. " +
                "Convert other types with HasConversion, e.g. HasConversion<string>()");

        property.SetValueConverter(converter);
    }

    // HasConversion<string>() only sets the provider type; the converter is otherwise chosen by the type mapping
    private ValueConverter? GetProviderTypeConversion(IConventionProperty property)
    {
        if (property.GetProviderClrType() is not { } providerType)
            return null;

        var modelType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        var conversions = converterSelector.Select(modelType, providerType).ToList();

        return conversions.Count > 0
            ? conversions[0].Create()
            : throw new EntityFrameworkEncryptionException(
                $"{DisplayName(property)}: no conversion from {modelType.Name} to {providerType.Name}");
    }

    // complex type display names are "Customer.Address#Address": use the CLR type name instead
    internal static string DisplayName(IReadOnlyProperty property)
        => property.DeclaringType is IReadOnlyEntityType entityType
            ? $"{entityType.DisplayName()}.{property.Name}"
            : $"{property.DeclaringType.ClrType.Name}.{property.Name}";

    private static IEnumerable<IConventionProperty> GetDeclaredProperties(IConventionTypeBase type)
        => type.GetDeclaredProperties()
            .Concat(type.GetDeclaredComplexProperties().SelectMany(x => GetDeclaredProperties(x.ComplexType)));

    [GeneratedRegex(@"\(\s*\d")]
    private static partial Regex SizedColumnType();

    private static bool IsEncrypted(IConventionProperty property)
        => property.FindAnnotation(PropertyAnnotations.IsEncrypted)?.Value is true
           || property.PropertyInfo?.GetCustomAttribute<EncryptedAttribute>(false) != null;

    private static string GetLabel(IConventionProperty property)
    {
        var label = property.FindAnnotation(PropertyAnnotations.Label)?.Value as string
                    ?? property.PropertyInfo?.GetCustomAttribute<EncryptedAttribute>(false)?.Label;

        if (label != null)
            return label;

        var entityType = property.DeclaringType.ContainingEntityType;

        // the column name within the table includes the complex property prefix: Address_Street
        return entityType.GetTableName() is { } table
            ? $"{table}.{property.GetColumnName(StoreObjectIdentifier.Table(table, entityType.GetSchema()))}"
            : $"{property.DeclaringType.ShortName()}.{property.Name}";
    }
}
