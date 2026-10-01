namespace EntityFrameworkCore.Encrypted.Annotations;

internal static class PropertyAnnotations
{
    public const string IsEncrypted = "Microsoft.EntityFrameworkCore.Encrypted.IsEncrypted";
    public const string Label = "Microsoft.EntityFrameworkCore.Encrypted.Label";
    public const string BlindIndex = "Microsoft.EntityFrameworkCore.Encrypted.BlindIndex";

    /// <summary>Normalization delegate: read and removed by the convention, so it never reaches migration snapshots.</summary>
    public const string BlindIndexNormalize = "Microsoft.EntityFrameworkCore.Encrypted.BlindIndexNormalize";
}
