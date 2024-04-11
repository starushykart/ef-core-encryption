namespace EntityFrameworkCore.Encryption.Common.Exceptions;

public class EntityFrameworkEncryptionException(string message)
    : Exception(message);