namespace EntityFrameworkCore.Encrypted;

/// <summary>
/// Reads values written before the library was used: encrypted by your previous code, or plaintext.
/// Used for stored values that aren't in the library's format, so the application can read them right away;
/// new values are always written in the library's format, and <c>ReEncryptAsync</c> migrates existing ones.
/// Remove it once <c>GetKeyUsageAsync</c> reports no values without a root key.
/// </summary>
/// <remarks>Implement the method for the column types you have: text columns, binary columns, or both.</remarks>
public interface ILegacyDecryptor
{
    /// <summary>Decrypts a value of a text column.</summary>
    /// <param name="storedValue">The value as stored in the column, e.g. your Base64 or hex ciphertext.</param>
    /// <returns>The plaintext, or <c>null</c> if the value isn't in your format.</returns>
    string? Decrypt(string storedValue, LegacyValueContext context) => null;

    /// <summary>Decrypts a value of a binary column.</summary>
    /// <inheritdoc cref="Decrypt(string, LegacyValueContext)"/>
    byte[]? Decrypt(byte[] storedValue, LegacyValueContext context) => null;
}

/// <summary>Where a legacy value is stored.</summary>
/// <param name="DbContextType">Context of the encrypted property.</param>
/// <param name="Label">Label of the encrypted column: <c>"{table}.{column}"</c> unless configured explicitly.</param>
public sealed record LegacyValueContext(Type DbContextType, string Label);

/// <summary>Built-in legacy decryptors.</summary>
public static class LegacyDecryptor
{
    /// <summary>
    /// For columns that were stored unencrypted: values that aren't in the library's format are read as they are.
    /// </summary>
    public static ILegacyDecryptor Plaintext { get; } = new PlaintextLegacyDecryptor();
}

internal sealed class PlaintextLegacyDecryptor : ILegacyDecryptor
{
    public string Decrypt(string storedValue, LegacyValueContext context)
        => storedValue;

    public byte[] Decrypt(byte[] storedValue, LegacyValueContext context)
        => storedValue;
}
