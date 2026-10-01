namespace EntityFrameworkCore.Encrypted.Annotations;

/// <summary>
/// Makes an encrypted property searchable by equality: a keyed hash (HMAC-SHA256) of the value is stored in an
/// indexed <c>{Property}_Index</c> column, and <c>==</c>, <c>!=</c> and <c>Contains</c> in queries compare it.
/// Values are matched exactly; to normalize them (e.g. ignore case) use <c>HasBlindIndex(normalize)</c>.
/// </summary>
/// <remarks>Reveals which rows have equal values: don't use it for columns with few distinct values.</remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class BlindIndexAttribute : Attribute;
