namespace EntityFrameworkCore.Encrypted.Common.Exceptions;

public class EntityFrameworkEncryptionException : Exception
{
    public EntityFrameworkEncryptionException(string message)
        : base(message)
    { }

    public EntityFrameworkEncryptionException(string message, Exception innerException)
        : base(message, innerException)
    { }
}
