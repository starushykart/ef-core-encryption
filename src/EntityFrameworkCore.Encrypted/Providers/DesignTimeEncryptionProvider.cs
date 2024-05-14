using EntityFrameworkCore.Encrypted.Common.Abstractions;

namespace EntityFrameworkCore.Encrypted.Providers;

internal class DesignTimeEncryptionProvider : IEncryptionProvider
{
    public byte[]? Encrypt(byte[]? input)
        => input;

    public byte[]? Decrypt(byte[]? input)
        => input;
}