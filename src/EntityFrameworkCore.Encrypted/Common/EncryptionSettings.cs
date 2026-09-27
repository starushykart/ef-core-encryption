namespace EntityFrameworkCore.Encrypted.Common;

/// <summary>Options configured with <see cref="EncryptionBuilder"/>.</summary>
internal sealed record EncryptionSettings
{
    public uint DataKeyVersion { get; init; }
    public TimeSpan? RootKeyRefreshInterval { get; init; }
    public bool CreateRootKeyIfMissing { get; init; } = true;
}
