using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

internal static class BlindIndex
{
    public static string PropertyName(string encryptedProperty)
        => encryptedProperty + "_Index";

    /// <summary>Blind index shadow property of an encrypted property, if it has one.</summary>
    public static IReadOnlyProperty? Find(IReadOnlyProperty encryptedProperty)
        => encryptedProperty.DeclaringType.FindProperty(PropertyName(encryptedProperty.Name)) is { } index
           && index.GetValueConverter() is IBlindIndexConverter
            ? index
            : null;
}
