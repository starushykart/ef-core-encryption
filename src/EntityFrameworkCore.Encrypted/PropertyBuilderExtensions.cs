using EntityFrameworkCore.Encrypted.Common;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.Encrypted;

public static class PropertyBuilderExtensions
{
    public static PropertyBuilder<TProperty> IsEncrypted<TProperty>(this PropertyBuilder<TProperty> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasAnnotation(PropertyAnnotations.IsEncrypted, true);
    }
}