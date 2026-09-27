namespace EntityFrameworkCore.Encrypted.Annotations;

/// <summary>Encrypts a <c>string</c> or <c>byte[]</c> property.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EncryptedAttribute : Attribute
{
    /// <summary>
    /// Value the ciphertext is bound to, so it can't be moved to another column. Defaults to <c>"{table}.{column}"</c>;
    /// set it explicitly to keep existing values readable after renaming the table or column.
    /// </summary>
    public string? Label { get; set; }
}
