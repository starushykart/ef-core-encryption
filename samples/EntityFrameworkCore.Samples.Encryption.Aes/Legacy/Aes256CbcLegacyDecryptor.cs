using EntityFrameworkCore.Encrypted;

namespace EntityFrameworkCore.Samples.Encryption.Aes.Legacy;

/// <summary>
/// Reads values written by the previous code while they're migrated to the library's format.
/// Remove it once <c>/keys/usage</c> no longer lists values without a root key.
/// </summary>
public sealed class Aes256CbcLegacyDecryptor(IConfiguration configuration) : ILegacyDecryptor
{
    private readonly byte[] _key = Convert.FromBase64String(configuration["Encryption:LegacyKey"]!);

    public string? Decrypt(string storedValue, LegacyValueContext context)
        => Aes256Cbc.TryDecrypt(storedValue, _key);
}
