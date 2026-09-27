namespace EntityFrameworkCore.Encrypted.Keys;

/// <summary>Number of stored values of a column encrypted with one key.</summary>
/// <param name="RootKeyId">Root key of the values, or <c>null</c> for values that are not valid encrypted values (e.g. plaintext).</param>
public sealed record KeyUsage(string Table, string Column, int? RootKeyId, uint? DataKeyVersion, long Values);
