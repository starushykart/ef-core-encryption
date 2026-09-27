using System.Reflection;
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

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

internal sealed class EncryptionConvention(DataKeyRing? keyRing, Type contextType)
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
            foreach (var property in entityType.GetDeclaredProperties().Where(IsEncrypted))
            {
                // the limit would apply to the ciphertext (plaintext + 35 bytes, Base64), not to the value
                if (property.GetMaxLength() != null)
                    throw new EntityFrameworkEncryptionException(
                        $"{entityType.DisplayName()}.{property.Name} is encrypted and can't have a maximum length: " +
                        "it would limit the stored ciphertext, not the value. Remove MaxLength/HasMaxLength from the property");

                var encryptor = new FieldEncryptor(keyRing, contextType, GetLabel(property));

                if (property.ClrType == typeof(string))
                    property.SetValueConverter(new StringEncryptionConverter(encryptor));
                else if (property.ClrType == typeof(byte[]))
                    property.SetValueConverter(new BinaryEncryptionConverter(encryptor));
                else
                    throw new EntityFrameworkEncryptionException(
                        $"Encryption can be applied only to string and byte[] properties, but {entityType.DisplayName()}.{property.Name} is {property.ClrType.Name}");
            }
        }
    }

    private static bool IsEncrypted(IConventionProperty property)
        => property.FindAnnotation(PropertyAnnotations.IsEncrypted)?.Value is true
           || property.PropertyInfo?.GetCustomAttribute<EncryptedAttribute>(false) != null;

    private static string GetLabel(IConventionProperty property)
    {
        var label = property.FindAnnotation(PropertyAnnotations.Label)?.Value as string
                    ?? property.PropertyInfo?.GetCustomAttribute<EncryptedAttribute>(false)?.Label;

        if (label != null)
            return label;

        var table = property.DeclaringType.ContainingEntityType.GetTableName();
        return table == null
            ? $"{property.DeclaringType.ShortName()}.{property.Name}"
            : $"{table}.{property.GetColumnName()}";
    }
}
