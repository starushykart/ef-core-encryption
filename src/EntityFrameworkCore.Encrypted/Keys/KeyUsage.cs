namespace EntityFrameworkCore.Encrypted.Keys;

/// <summary>Number of stored values of a column encrypted with one key.</summary>
/// <param name="Table">Table of the column, with its schema if it has one.</param>
/// <param name="Column">Column name.</param>
/// <param name="RootKeyId">Root key of the values, or <c>null</c> for values that are not valid encrypted values (e.g. plaintext).</param>
/// <param name="DataKeyVersion">Data key version of the values, or <c>null</c> for values that are not valid encrypted values.</param>
/// <param name="Values">Number of values.</param>
public sealed record KeyUsage(string Table, string Column, int? RootKeyId, uint? DataKeyVersion, long Values);
