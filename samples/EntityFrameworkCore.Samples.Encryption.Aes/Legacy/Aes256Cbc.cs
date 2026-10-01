using System.Security.Cryptography;
using System.Text;

namespace EntityFrameworkCore.Samples.Encryption.Aes.Legacy;

/// <summary>
/// The "previous code": plain AES-256-CBC with a random IV, stored as Base64 of <c>[IV 16B][ciphertext]</c>.
/// That's also the format of EntityFrameworkCore.Encrypted 1.x, so this works for upgrading from it.
/// </summary>
public static class Aes256Cbc
{
    private const int BlockSize = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Encrypt(string value, byte[] key)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        var ciphertext = aes.EncryptCbc(Encoding.UTF8.GetBytes(value), aes.IV);
        return Convert.ToBase64String([..aes.IV, ..ciphertext]);
    }

    /// <returns>The plaintext, or <c>null</c> if the value isn't in this format or was encrypted with another key.</returns>
    public static string? TryDecrypt(string stored, byte[] key)
    {
        var buffer = new byte[stored.Length];

        // IV plus at least one block, whole blocks only
        if (!Convert.TryFromBase64String(stored, buffer, out var length) || length < 2 * BlockSize || length % BlockSize != 0)
            return null;

        try
        {
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = key;

            var plaintext = aes.DecryptCbc(buffer.AsSpan(BlockSize, length - BlockSize), buffer.AsSpan(0, BlockSize));

            // CBC isn't authenticated: a wrong key or another format usually fails the padding or UTF-8 check
            return StrictUtf8.GetString(plaintext);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return null;
        }
    }
}
