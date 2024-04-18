namespace EntityFrameworkCore.Encryption.Common;

public class EntityFrameworkEncryptionException(string message)
    : Exception(message);