namespace EntityFrameworkCore.Encrypted.Common.Abstractions;

internal interface IEncryptionProvider
{
    byte[]? Encrypt(byte[]? input);

    byte[]? Decrypt(byte[]? input);
}
