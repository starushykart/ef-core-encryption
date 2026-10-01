using System.Buffers;
using System.Security.Cryptography;
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
        x => Encrypt(encryptor, x),
        x => Decrypt(encryptor, x)), IEncryptionConverter
{
    private const int MaxStackSize = 1024;

    public FieldEncryptor Encryptor => encryptor;

    public byte[] ToEnvelope(object providerValue)
        => Convert.FromBase64String((string)providerValue);

    public object FromEnvelope(byte[] envelope)
        => Convert.ToBase64String(envelope);

    // the result string is the only allocation: UTF-8 bytes and the envelope use stack or pooled memory
    private static string Encrypt(FieldEncryptor encryptor, string value)
    {
        var plaintextLength = Encoding.UTF8.GetByteCount(value);
        var length = plaintextLength + Envelope.GetLength(plaintextLength);
        var rented = length > MaxStackSize ? ArrayPool<byte>.Shared.Rent(length) : null;
        var buffer = rented != null ? rented.AsSpan(0, length) : stackalloc byte[length];

        var plaintext = buffer[..plaintextLength];
        var envelope = buffer[plaintextLength..];

        try
        {
            Encoding.UTF8.GetBytes(value, plaintext);
            encryptor.Encrypt(plaintext, envelope);
            return Convert.ToBase64String(envelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);

            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static string Decrypt(FieldEncryptor encryptor, string value)
    {
        // decoded length is at most 3/4 of the Base64 length; the plaintext is 35 bytes shorter than the envelope
        var maxEnvelopeLength = value.Length / 4 * 3;
        var length = maxEnvelopeLength * 2;
        var rented = length > MaxStackSize ? ArrayPool<byte>.Shared.Rent(length) : null;
        var buffer = rented != null ? rented.AsSpan(0, length) : stackalloc byte[length];

        scoped Span<byte> plaintext = default;

        try
        {
            if (!Convert.TryFromBase64String(value, buffer[..maxEnvelopeLength], out var envelopeLength))
                throw new FormatException("Encrypted value is not valid Base64");

            var envelope = buffer[..envelopeLength];
            plaintext = buffer.Slice(maxEnvelopeLength, Envelope.GetPlaintextLength(envelopeLength));

            encryptor.Decrypt(envelope, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);

            if (rented != null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
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
