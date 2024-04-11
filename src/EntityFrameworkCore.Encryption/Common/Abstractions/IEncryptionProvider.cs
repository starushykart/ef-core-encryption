using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Common.Abstractions;

public interface IEncryptionProvider
{
    byte[]? Encrypt(byte[]? input);

    byte[]? Decrypt(byte[]? input);
}

public interface IEncryptionProvider<TContext> : IEncryptionProvider
    where TContext : DbContext;