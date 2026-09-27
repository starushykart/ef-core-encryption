using System.Text;
using EntityFrameworkCore.Encrypted.Common.Crypto;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

internal interface IEncryptionConverter
{
    FieldEncryptor Encryptor { get; }

    /// <summary>Encrypted value as stored in the database (Base64 text or binary) to its raw envelope.</summary>
    byte[] ToEnvelope(object providerValue);

    object FromEnvelope(byte[] envelope);
}

/// <summary>Stores encrypted strings as Base64 text.</summary>
internal sealed class StringEncryptionConverter(FieldEncryptor encryptor)
    : ValueConverter<string, string>(
        x => Convert.ToBase64String(encryptor.Encrypt(Encoding.UTF8.GetBytes(x))),
        x => Encoding.UTF8.GetString(encryptor.Decrypt(Convert.FromBase64String(x)))), IEncryptionConverter
{
    public FieldEncryptor Encryptor => encryptor;

    public byte[] ToEnvelope(object providerValue)
        => Convert.FromBase64String((string)providerValue);

    public object FromEnvelope(byte[] envelope)
        => Convert.ToBase64String(envelope);
}

/// <summary>Stores encrypted binary values as binary.</summary>
internal sealed class BinaryEncryptionConverter(FieldEncryptor encryptor)
    : ValueConverter<byte[], byte[]>(
        x => encryptor.Encrypt(x),
        x => encryptor.Decrypt(x)), IEncryptionConverter
{
    public FieldEncryptor Encryptor => encryptor;

    public byte[] ToEnvelope(object providerValue)
        => (byte[])providerValue;

    public object FromEnvelope(byte[] envelope)
        => envelope;
}
