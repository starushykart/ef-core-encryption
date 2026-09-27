using System.Security.Cryptography;
using System.Text;
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

    public string Label => label;

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var (keyId, key) = GetKeyRing().GetEncryptionKey(contextType);
        return Envelope.Seal(key, keyId, _label, plaintext);
    }

    public byte[] Decrypt(ReadOnlySpan<byte> envelope)
    {
        var keyId = Envelope.ReadKeyId(envelope);
        var key = GetKeyRing().GetDecryptionKey(contextType, keyId);

        try
        {
            return Envelope.Open(key, _label, envelope);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new EntityFrameworkEncryptionException(
                $"Can't decrypt value of '{label}' ({keyId}): it was modified or encrypted for another column", ex);
        }
    }

    private DataKeyRing GetKeyRing()
        => keyRing ?? throw new EntityFrameworkEncryptionException(
            $"Encryption is not configured for {contextType.Name}. " +
            "Register it with services.AddEncryption(...) and create the context through dependency injection");
}
