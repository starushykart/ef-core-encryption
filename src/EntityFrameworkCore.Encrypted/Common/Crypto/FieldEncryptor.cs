using System.Security.Cryptography;
using System.Text;
using EntityFrameworkCore.Encrypted.Common.Diagnostics;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;

namespace EntityFrameworkCore.Encrypted.Common.Crypto;

/// <summary>Encrypts values of one column: keys of the context, ciphertext bound to the column label.</summary>
/// <param name="keyRing">
/// <c>null</c> when the context is created outside of dependency injection: the model is built, but values can't be encrypted.
/// </param>
internal sealed class FieldEncryptor(DataKeyRing? keyRing, Type contextType, string label)
{
    private readonly byte[] _label = Encoding.UTF8.GetBytes(label);
    private readonly string _contextName = contextType.Name;
    private LegacyValueContext? _legacyContext;

    public string Label => label;

    /// <param name="envelope">Destination of <see cref="Envelope.GetLength"/> bytes.</param>
    public void Encrypt(ReadOnlySpan<byte> plaintext, Span<byte> envelope)
    {
        var (keyId, key) = GetKeyRing().GetEncryptionKey(contextType);
        Envelope.Seal(key, keyId, _label, plaintext, envelope);
        Telemetry.RecordValue(_contextName, Telemetry.Operations.Encrypt);
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var envelope = new byte[Envelope.GetLength(plaintext.Length)];
        Encrypt(plaintext, envelope);
        return envelope;
    }

    /// <param name="plaintext">Destination of <see cref="Envelope.GetPlaintextLength"/> bytes.</param>
    public void Decrypt(ReadOnlySpan<byte> envelope, Span<byte> plaintext)
    {
        KeyId keyId;
        DataKey key;

        try
        {
            keyId = Envelope.ReadKeyId(envelope);
        }
        catch (EntityFrameworkEncryptionException)
        {
            RecordInvalidFormat();
            throw;
        }

        try
        {
            key = GetKeyRing().GetDecryptionKey(contextType, keyId);
        }
        catch (EntityFrameworkEncryptionException)
        {
            Telemetry.RecordDecryptionFailure(_contextName, "key_not_found");
            throw;
        }

        try
        {
            Envelope.Open(key, _label, envelope, plaintext);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            Telemetry.RecordDecryptionFailure(_contextName, "tampered");
            throw new EntityFrameworkEncryptionException(
                $"Can't decrypt value of '{label}' ({keyId}): it was modified or encrypted for another column", ex);
        }

        Telemetry.RecordValue(_contextName, Telemetry.Operations.Decrypt);
    }

    /// <inheritdoc cref="Envelope.GetPlaintextLength"/>
    public int GetPlaintextLength(int envelopeLength)
    {
        try
        {
            return Envelope.GetPlaintextLength(envelopeLength);
        }
        catch (EntityFrameworkEncryptionException)
        {
            RecordInvalidFormat();
            throw;
        }
    }

    /// <summary>Counts a stored value that isn't an encrypted value, e.g. invalid Base64 or plaintext.</summary>
    public void RecordInvalidFormat()
        => Telemetry.RecordDecryptionFailure(_contextName, "invalid_format");

    public byte[] Decrypt(ReadOnlySpan<byte> envelope)
    {
        var plaintext = new byte[GetPlaintextLength(envelope.Length)];
        Decrypt(envelope, plaintext);
        return plaintext;
    }

    /// <summary>Decrypts a stored binary value: in the library's format, or with the legacy decryptor.</summary>
    public byte[] DecryptStored(byte[] stored)
        => TryDecryptLegacy(stored, out var legacy) ? legacy! : Decrypt(stored);

    /// <summary>Reads a stored text value that isn't in the library's format with the legacy decryptor.</summary>
    /// <returns><c>false</c> if there is no legacy decryptor, or the value is in the library's format.</returns>
    public bool TryDecryptLegacy(string stored, out string? plaintext)
    {
        plaintext = null;

        if (keyRing?.LegacyDecryptor is not { } legacy)
            return false;

        var decoded = new byte[stored.Length / 4 * 3];
        var format = Convert.TryFromBase64String(stored, decoded, out var length)
            ? Classify(decoded.AsSpan(0, length), legacy)
            : StoredFormat.Legacy;

        plaintext = Call(format, () => legacy.Decrypt(stored, LegacyContext));
        return plaintext != null;
    }

    /// <summary>Reads a stored binary value that isn't in the library's format with the legacy decryptor.</summary>
    /// <returns><c>false</c> if there is no legacy decryptor, or the value is in the library's format.</returns>
    public bool TryDecryptLegacy(byte[] stored, out byte[]? plaintext)
    {
        plaintext = null;

        if (keyRing?.LegacyDecryptor is not { } legacy)
            return false;

        plaintext = Call(Classify(stored, legacy), () => legacy.Decrypt(stored, LegacyContext));
        return plaintext != null;
    }

    /// <summary>Key of a value in the library's format; <c>null</c> for legacy values and values in another format.</summary>
    public KeyId? TryReadKeyId(ReadOnlySpan<byte> envelope)
        => !Envelope.HasHeader(envelope) ? null
            : keyRing?.LegacyDecryptor is { } legacy && Classify(envelope, legacy) != StoredFormat.Current ? null
            : Envelope.ReadKeyId(envelope);

    private LegacyValueContext LegacyContext => _legacyContext ??= new LegacyValueContext(contextType, label);

    // the library's format: the format byte, long enough, and a data key version that has been configured. Legacy
    // ciphertext matches by chance about once in 2^40 values; a value in the library's format that fails to decrypt
    // is never passed to the legacy decryptor, so tampering is still detected
    private StoredFormat Classify(ReadOnlySpan<byte> decoded, ILegacyDecryptor legacy)
    {
        if (!Envelope.HasHeader(decoded))
            return StoredFormat.Legacy;

        if (Envelope.ReadKeyId(decoded).DataKeyVersion <= keyRing!.DataKeyVersion)
            return StoredFormat.Current;

        // a newer data key version than configured: a newer deployment, or legacy ciphertext. Plaintext would
        // return the ciphertext as the value, so only decryptors that recognize their own format are asked
        return legacy is PlaintextLegacyDecryptor ? StoredFormat.Current : StoredFormat.Unclear;
    }

    private T? Call<T>(StoredFormat format, Func<T?> decrypt) where T : class
    {
        if (format == StoredFormat.Current)
            return null;

        T? plaintext;

        try
        {
            plaintext = decrypt();
        }
        catch (Exception) when (format == StoredFormat.Unclear)
        {
            // most likely in the library's format after all: decrypted as such
            return null;
        }
        catch (Exception ex) when (ex is not EntityFrameworkEncryptionException)
        {
            throw new EntityFrameworkEncryptionException($"Legacy decryptor failed to decrypt value of '{label}'", ex);
        }

        if (plaintext != null)
            Telemetry.RecordLegacyValue(_contextName);

        return plaintext;
    }

    private enum StoredFormat
    {
        Current,
        Legacy,

        /// <summary>The library's format, but a data key version newer than configured.</summary>
        Unclear
    }

    private DataKeyRing GetKeyRing()
        => keyRing ?? throw new EntityFrameworkEncryptionException(
            $"Encryption is not configured for {contextType.Name}. " +
            "Register it with services.AddEncryption(...) and create the context through dependency injection");
}
