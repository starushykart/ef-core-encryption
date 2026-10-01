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
            Telemetry.RecordDecryptionFailure(_contextName, "invalid_format");
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

    public byte[] Decrypt(ReadOnlySpan<byte> envelope)
    {
        var plaintext = new byte[Envelope.GetPlaintextLength(envelope.Length)];
        Decrypt(envelope, plaintext);
        return plaintext;
    }

    private DataKeyRing GetKeyRing()
        => keyRing ?? throw new EntityFrameworkEncryptionException(
            $"Encryption is not configured for {contextType.Name}. " +
            "Register it with services.AddEncryption(...) and create the context through dependency injection");
}
