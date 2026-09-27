using System.Buffers.Binary;
using System.Security.Cryptography;
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

    private const int HeaderSize = 7;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Overhead = HeaderSize + NonceSize + TagSize;

    public static byte[] Seal(ReadOnlySpan<byte> key, KeyId keyId, ReadOnlySpan<byte> label, ReadOnlySpan<byte> plaintext)
    {
        var envelope = new byte[Overhead + plaintext.Length];
        var span = envelope.AsSpan();

        span[0] = FormatVersion;
        BinaryPrimitives.WriteUInt16BigEndian(span[1..3], keyId.RootKeyId);
        BinaryPrimitives.WriteUInt32BigEndian(span[3..HeaderSize], keyId.DataKeyVersion);

        var nonce = span.Slice(HeaderSize, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(
            nonce,
            plaintext,
            span.Slice(HeaderSize + NonceSize, plaintext.Length),
            span[^TagSize..],
            AssociatedData(span[..HeaderSize], label));

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

    /// <exception cref="AuthenticationTagMismatchException">Wrong key or label, or the value was tampered with.</exception>
    public static byte[] Open(ReadOnlySpan<byte> key, ReadOnlySpan<byte> label, ReadOnlySpan<byte> envelope)
    {
        var plaintext = new byte[envelope.Length - Overhead];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            envelope.Slice(HeaderSize, NonceSize),
            envelope.Slice(HeaderSize + NonceSize, plaintext.Length),
            envelope[^TagSize..],
            plaintext,
            AssociatedData(envelope[..HeaderSize], label));

        return plaintext;
    }

    private static byte[] AssociatedData(ReadOnlySpan<byte> header, ReadOnlySpan<byte> label)
    {
        var associatedData = new byte[header.Length + label.Length];
        header.CopyTo(associatedData);
        label.CopyTo(associatedData.AsSpan(header.Length));
        return associatedData;
    }
}
