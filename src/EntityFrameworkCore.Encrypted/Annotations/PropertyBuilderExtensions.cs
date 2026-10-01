using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.Encrypted.Annotations;

public static class PropertyBuilderExtensions
{
    /// <summary>Encrypts a <c>string</c> or <c>byte[]</c> property, or one converted to them with <c>HasConversion</c>.</summary>
    /// <param name="label">
    /// Value the ciphertext is bound to, so it can't be moved to another column. Defaults to <c>"{table}.{column}"</c>;
    /// set it explicitly to keep existing values readable after renaming the table or column.
    /// </param>
    public static PropertyBuilder<TProperty> IsEncrypted<TProperty>(this PropertyBuilder<TProperty> builder, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasAnnotation(PropertyAnnotations.IsEncrypted, true);

        if (label != null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
            builder.HasAnnotation(PropertyAnnotations.Label, label);
        }

        return builder;
    }

    /// <inheritdoc cref="IsEncrypted{TProperty}(PropertyBuilder{TProperty}, string?)"/>
    public static ComplexTypePropertyBuilder<TProperty> IsEncrypted<TProperty>(this ComplexTypePropertyBuilder<TProperty> builder, string? label = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasAnnotation(PropertyAnnotations.IsEncrypted, true);

        if (label != null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
            builder.HasAnnotation(PropertyAnnotations.Label, label);
        }

        return builder;
    }

    /// <summary>
    /// Makes an encrypted property searchable by equality: a keyed hash (HMAC-SHA256) of the value is stored in an
    /// indexed <c>{Property}_Index</c> column, and <c>==</c>, <c>!=</c> and <c>Contains</c> in queries compare it.
    /// </summary>
    /// <param name="normalize">
    /// Applied to the value (after a configured conversion) before hashing, on save and in queries,
    /// e.g. <c>v => v.Trim().ToLowerInvariant()</c> to find values regardless of case. Changing it later requires
    /// <c>RebuildBlindIndexesAsync</c>. Only for values stored as strings.
    /// </param>
    /// <remarks>Reveals which rows have equal values: don't use it for columns with few distinct values.</remarks>
    public static PropertyBuilder<TProperty> HasBlindIndex<TProperty>(this PropertyBuilder<TProperty> builder, Func<string, string>? normalize = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasAnnotation(PropertyAnnotations.BlindIndex, true);

        if (normalize != null)
            builder.HasAnnotation(PropertyAnnotations.BlindIndexNormalize, normalize);

        return builder;
    }
}
