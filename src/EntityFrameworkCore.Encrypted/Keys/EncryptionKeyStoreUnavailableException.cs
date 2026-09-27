using EntityFrameworkCore.Encrypted.Common.Exceptions;

namespace EntityFrameworkCore.Encrypted.Keys;

/// <summary>
/// The key store cannot be read yet, e.g. its table is created by migrations that run after the host starts.
/// Keys are then loaded on first use instead of on startup.
/// </summary>
public class EncryptionKeyStoreUnavailableException(string message, Exception innerException)
    : EntityFrameworkEncryptionException(message, innerException);
