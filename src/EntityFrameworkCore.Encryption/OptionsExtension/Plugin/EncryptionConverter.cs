using System.Text;
using EntityFrameworkCore.Encryption.Common.Abstractions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EntityFrameworkCore.Encryption.OptionsExtension.Plugin;

internal sealed class EncryptionConverter(IEncryptionProvider encryptionProvider) 
    : ValueConverter<string?, string?>(
        x => Encrypt(x, encryptionProvider),
        x => Decrypt(x, encryptionProvider))
{
    private const char NullChar = '\0';
    private static string? Encrypt(string? input, IEncryptionProvider encryptionProvider)
    {
        if (input == null)
            return default;

        var inputData = Encoding.UTF8.GetBytes(input);
        var encryptedRawBytes = encryptionProvider.Encrypt(inputData);

        return encryptedRawBytes is not null
            ? Convert.ToBase64String(encryptedRawBytes)
            : null;
    }

    private static string? Decrypt(string? input, IEncryptionProvider encryptionProvider)
    {
        if (input == null)
            return default;

        var inputData = Convert.FromBase64String(input);
        var decryptedBytes = encryptionProvider.Decrypt(inputData);

        return decryptedBytes == null || decryptedBytes.Length == 0
            ? default!
            : Encoding.UTF8.GetString(decryptedBytes).Trim(NullChar);
    }
}