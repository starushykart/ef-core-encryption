using System.Reflection;
using System.Text.RegularExpressions;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Crypto;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Common.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            foreach (var property in GetDeclaredProperties(entityType).ToList())
            {
                // a delegate can't be written to migration snapshots: it's only kept in the blind index converter
                var normalize = property.FindAnnotation(PropertyAnnotations.BlindIndexNormalize)?.Value as Func<string, string>;
                property.RemoveAnnotation(PropertyAnnotations.BlindIndexNormalize);

                if (!IsEncrypted(property))
                {
                    if (HasBlindIndex(property))
                        throw new EntityFrameworkEncryptionException(
                            $"{DisplayName(property)} has a blind index but isn't encrypted. Mark it with [Encrypted] or IsEncrypted()");

                    continue;
                }

                var conversion = Encrypt(property);

                if (HasBlindIndex(property))
                    AddBlindIndex(property, conversion, normalize);
            }

            RejectSeedData(entityType);
        }
    }

    // seed data is inserted by migrations: values would be encrypted at design time (without keys) into the migration,
    // differently every time, and blind indexes wouldn't be set
    private static void RejectSeedData(IConventionEntityType entityType)
    {
        var encrypted = entityType.GetDeclaredProperties().Where(x => x.GetValueConverter() is IEncryptionConverter).Select(x => x.Name).ToHashSet();

        if (encrypted.Count > 0
            && entityType.GetSeedData().Any(row => row.Any(x => x.Value != null && encrypted.Contains(x.Key))))
            throw new EntityFrameworkEncryptionException(
                $"{entityType.DisplayName()} has seed data (HasData) for encrypted properties. Seed encrypted values from code " +
                "with SaveChanges instead, e.g. on startup");
    }

    /// <returns>The configured conversion that runs before encryption, if any.</returns>
    private ValueConverter? Encrypt(IConventionProperty property)
    {
        var name = DisplayName(property);

        // the same value encrypts differently every time: the database can't match, reference or compare it
        if (property.IsKey() || property.IsForeignKey())
            throw new EntityFrameworkEncryptionException(
                $"{name} is part of a key or foreign key and can't be encrypted: encrypted values differ every time, " +
                "so they can't identify or reference rows");

        if (property.IsConcurrencyToken)
            throw new EntityFrameworkEncryptionException(
                $"{name} is a concurrency token and can't be encrypted: its stored value changes on every save, so every update would conflict");

        if (property.GetContainingIndexes().Any(x => x.IsUnique))
            throw new EntityFrameworkEncryptionException(
                $"{name} is encrypted and can't have a unique index: encrypted values differ every time, so uniqueness wouldn't be enforced");

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

        // with a converter EF compares arrays by reference: changes made inside an array wouldn't be saved
        if (property.ClrType == typeof(byte[]) && property.GetValueComparer() == null)
            property.SetValueComparer(new ArrayStructuralComparer<byte>());

        return conversion;
    }

    /// <summary>
    /// Shadow <c>{Property}_Index</c> property holding the same value, hashed by its converter: filled on save by
    /// <see cref="BlindIndexSaveChangesInterceptor"/>, compared instead of the encrypted column by <see cref="EncryptedQueryGuard"/>.
    /// </summary>
    private void AddBlindIndex(IConventionProperty property, ValueConverter? conversion, Func<string, string>? normalize)
    {
        // already encrypted (the model is finalized again): the blind index was added then
        if (conversion is IEncryptionConverter)
            return;

        var name = DisplayName(property);

        if (property.DeclaringType is not IConventionEntityType entityType)
            throw new EntityFrameworkEncryptionException(
                $"{name}: blind indexes aren't supported on properties of complex types");

        // stored as text inside the JSON document, compared as binary: lookups would never match
        if (entityType.IsMappedToJson())
            throw new EntityFrameworkEncryptionException(
                $"{name}: blind indexes aren't supported on types mapped to JSON");

        var providerType = conversion?.ProviderClrType ?? property.ClrType;

        if (normalize != null && providerType != typeof(string))
            throw new EntityFrameworkEncryptionException($"{name}: blind index normalization is only supported for values stored as strings");

        var indexName = BlindIndex.PropertyName(property.Name);

        if (entityType.FindProperty(indexName) is { } existing && existing.GetValueConverter() is not IBlindIndexConverter)
            throw new EntityFrameworkEncryptionException(
                $"{name} has a blind index, but {entityType.DisplayName()} already has a property named {indexName}");

        var indexer = new BlindIndexer(keyRing, contextType, GetLabel(property), normalize);
        var modelType = conversion?.ModelClrType ?? property.ClrType;
        var converter = (ValueConverter)Activator.CreateInstance(
            typeof(BlindIndexConverter<>).MakeGenericType(modelType), indexer, conversion, property.Name)!;

        // always nullable: rows that existed before the blind index was added have none until it's rebuilt
        var indexType = property.ClrType.IsValueType && Nullable.GetUnderlyingType(property.ClrType) == null
            ? typeof(Nullable<>).MakeGenericType(property.ClrType)
            : property.ClrType;

        var index = entityType.Builder.Property(indexType, indexName, fromDataAnnotation: true)!;
        index.IsRequired(false, fromDataAnnotation: true);
        index.HasConversion(converter, fromDataAnnotation: true);
        index.HasMaxLength(BlindIndexer.Size, fromDataAnnotation: true);

        if (property.GetValueComparer() is { } comparer)
            index.HasValueComparer(comparer, fromDataAnnotation: true);

        entityType.Builder.HasIndex([index.Metadata], fromDataAnnotation: true);
        keyRing?.UseBlindIndexes(contextType);
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

    private static bool HasBlindIndex(IConventionProperty property)
        => property.FindAnnotation(PropertyAnnotations.BlindIndex)?.Value is true
           || property.PropertyInfo?.GetCustomAttribute<BlindIndexAttribute>(false) != null;

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
