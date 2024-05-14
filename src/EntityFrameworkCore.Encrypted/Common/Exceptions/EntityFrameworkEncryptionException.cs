namespace EntityFrameworkCore.Encrypted.Common.Exceptions;

public class EntityFrameworkEncryptionException(string message)
    : Exception(message);