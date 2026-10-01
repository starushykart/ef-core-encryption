namespace EntityFrameworkCore.Encrypted.Common.Exceptions;

/// <summary>
/// Thrown for encryption errors: invalid configuration or model, keys that can't be loaded, values that can't be
/// decrypted, and queries the database would evaluate on ciphertext.
/// </summary>
public class EntityFrameworkEncryptionException : Exception
{
    /// <inheritdoc cref="Exception(string)"/>
    public EntityFrameworkEncryptionException(string message)
        : base(message)
    { }

    /// <inheritdoc cref="Exception(string, Exception)"/>
    public EntityFrameworkEncryptionException(string message, Exception innerException)
        : base(message, innerException)
    { }
}
