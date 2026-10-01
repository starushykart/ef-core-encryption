using System.Buffers.Binary;
using EntityFrameworkCore.Encrypted.Common.Exceptions;

namespace EntityFrameworkCore.Encrypted.Common.Crypto;

/// <summary>
/// AES-256-GCM encrypted value:
/// <c>[format 1B][root key id 2B][data key version 4B][nonce 12B][ciphertext][tag 16B]</c>.
/// The header and the column label are authenticated as associated data.
/// </summary>
internal static class Envelope
{
    public const byte FormatVersion = 1;
    public const int KeySize = 32;
    public const int TagSize = 16;

    private const int HeaderSize = 7;
    private const int NonceSize = 12;
    private const int Overhead = HeaderSize + NonceSize + TagSize;
    private const int MaxStackAssociatedData = 256;

    public static int GetLength(int plaintextLength)
        => Overhead + plaintextLength;

    /// <exception cref="EntityFrameworkEncryptionException">Too short to be an encrypted value.</exception>
    public static int GetPlaintextLength(int envelopeLength)
        => envelopeLength >= Overhead
            ? envelopeLength - Overhead
            : throw new EntityFrameworkEncryptionException("Encrypted value is corrupted or has an unsupported format");

    /// <param name="envelope">Destination of <see cref="GetLength"/> bytes.</param>
    public static void Seal(DataKey key, KeyId keyId, ReadOnlySpan<byte> label, ReadOnlySpan<byte> plaintext, Span<byte> envelope)
    {
        envelope[0] = FormatVersion;
        BinaryPrimitives.WriteUInt16BigEndian(envelope[1..3], keyId.RootKeyId);
        BinaryPrimitives.WriteUInt32BigEndian(envelope[3..HeaderSize], keyId.DataKeyVersion);

        var nonce = envelope.Slice(HeaderSize, NonceSize);
        System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);

        var length = HeaderSize + label.Length;
        Span<byte> associatedData = length <= MaxStackAssociatedData ? stackalloc byte[length] : new byte[length];
        WriteAssociatedData(envelope[..HeaderSize], label, associatedData);

        key.Cipher.Encrypt(
            nonce,
            plaintext,
            envelope.Slice(HeaderSize + NonceSize, plaintext.Length),
            envelope.Slice(HeaderSize + NonceSize + plaintext.Length, TagSize),
            associatedData);
    }

    public static byte[] Seal(DataKey key, KeyId keyId, ReadOnlySpan<byte> label, ReadOnlySpan<byte> plaintext)
    {
        var envelope = new byte[GetLength(plaintext.Length)];
        Seal(key, keyId, label, plaintext, envelope);
        return envelope;
    }

    public static KeyId ReadKeyId(ReadOnlySpan<byte> envelope)
    {
        if (envelope.Length < Overhead || envelope[0] != FormatVersion)
            throw new EntityFrameworkEncryptionException("Encrypted value is corrupted or has an unsupported format");

        return new KeyId(
            BinaryPrimitives.ReadUInt16BigEndian(envelope[1..3]),
            BinaryPrimitives.ReadUInt32BigEndian(envelope[3..HeaderSize]));
    }

    /// <param name="plaintext">Destination of <see cref="GetPlaintextLength"/> bytes.</param>
    /// <exception cref="System.Security.Cryptography.AuthenticationTagMismatchException">Wrong key or label, or the value was tampered with.</exception>
    public static void Open(DataKey key, ReadOnlySpan<byte> label, ReadOnlySpan<byte> envelope, Span<byte> plaintext)
    {
        var length = HeaderSize + label.Length;
        Span<byte> associatedData = length <= MaxStackAssociatedData ? stackalloc byte[length] : new byte[length];
        WriteAssociatedData(envelope[..HeaderSize], label, associatedData);

        key.Cipher.Decrypt(
            envelope.Slice(HeaderSize, NonceSize),
            envelope.Slice(HeaderSize + NonceSize, plaintext.Length),
            envelope[^TagSize..],
            plaintext,
            associatedData);
    }

    public static byte[] Open(DataKey key, ReadOnlySpan<byte> label, ReadOnlySpan<byte> envelope)
    {
        var plaintext = new byte[GetPlaintextLength(envelope.Length)];
        Open(key, label, envelope, plaintext);
        return plaintext;
    }

    private static void WriteAssociatedData(ReadOnlySpan<byte> header, ReadOnlySpan<byte> label, Span<byte> destination)
    {
        header.CopyTo(destination);
        label.CopyTo(destination[header.Length..]);
    }
}
