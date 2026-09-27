using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.Encrypted.Annotations;

public static class PropertyBuilderExtensions
{
    /// <summary>Encrypts a <c>string</c> or <c>byte[]</c> property.</summary>
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
}
