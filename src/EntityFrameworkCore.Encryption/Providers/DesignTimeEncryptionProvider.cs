using EntityFrameworkCore.Encryption.Common.Abstractions;

namespace EntityFrameworkCore.Encryption.Providers;

internal class DesignTimeEncryptionProvider : IEncryptionProvider
{
    public byte[]? Encrypt(byte[]? input)
        => input;

    public byte[]? Decrypt(byte[]? input)
        => input;
}