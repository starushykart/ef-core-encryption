using System.Text;
using EntityFrameworkCore.Encrypted.Common.Crypto;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

/// <summary>Stores encrypted strings as Base64 text.</summary>
internal sealed class StringEncryptionConverter(FieldEncryptor encryptor)
    : ValueConverter<string, string>(
        x => Convert.ToBase64String(encryptor.Encrypt(Encoding.UTF8.GetBytes(x))),
        x => Encoding.UTF8.GetString(encryptor.Decrypt(Convert.FromBase64String(x))))
{
    public FieldEncryptor Encryptor => encryptor;
}

/// <summary>Stores encrypted binary values as binary.</summary>
internal sealed class BinaryEncryptionConverter(FieldEncryptor encryptor)
    : ValueConverter<byte[], byte[]>(
        x => encryptor.Encrypt(x),
        x => encryptor.Decrypt(x))
{
    public FieldEncryptor Encryptor => encryptor;
}
