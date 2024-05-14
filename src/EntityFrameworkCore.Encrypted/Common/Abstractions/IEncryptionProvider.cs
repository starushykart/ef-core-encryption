namespace EntityFrameworkCore.Encrypted.Common.Abstractions;

public interface IEncryptionProvider
{
    byte[]? Encrypt(byte[]? input);

    byte[]? Decrypt(byte[]? input);
}