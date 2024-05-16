using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntityFrameworkCore.Encrypted.Annotations;

public static class PropertyBuilderExtensions
{
    public static PropertyBuilder<TProperty> IsEncrypted<TProperty>(this PropertyBuilder<TProperty> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasAnnotation(PropertyAnnotations.IsEncrypted, true);
    }
}