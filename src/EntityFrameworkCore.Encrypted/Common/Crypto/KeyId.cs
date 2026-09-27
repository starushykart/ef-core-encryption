namespace EntityFrameworkCore.Encrypted.Common.Crypto;

/// <summary>Identifies the data key of an encrypted value: root key id and derived data key version.</summary>
internal readonly record struct KeyId(ushort RootKeyId, uint DataKeyVersion)
{
    public override string ToString() => $"root {RootKeyId}, v{DataKeyVersion}";
}
