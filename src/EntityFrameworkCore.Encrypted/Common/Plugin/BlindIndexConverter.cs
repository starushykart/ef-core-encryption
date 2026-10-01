using System.Security.Cryptography;
using System.Text;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

/// <summary>
/// Blind index of an encrypted column: HMAC-SHA256 of the value as stored before encryption (after its configured
/// conversion and the normalization), keyed with a key derived per column from the blind index key of the context.
/// </summary>
internal sealed class BlindIndexer(DataKeyRing? keyRing, Type contextType, string label, Func<string, string>? normalize)
{
    public const int Size = 32;

    public string Label => label;

    /// <param name="value">String or binary value, as encrypted.</param>
    public byte[] Compute(object value)
        => value switch
        {
            string text => HMACSHA256.HashData(GetKey(), Encoding.UTF8.GetBytes(Normalize(text))),
            byte[] binary => HMACSHA256.HashData(GetKey(), binary),
            _ => throw new EntityFrameworkEncryptionException($"Blind index of {label}: unsupported value {value.GetType().Name}")
        };

    /// <param name="plaintext">Decrypted value: UTF-8 text when <paramref name="isText"/>.</param>
    public byte[] ComputeFromPlaintext(byte[] plaintext, bool isText)
        => isText ? Compute(Encoding.UTF8.GetString(plaintext)) : Compute(plaintext);

    private string Normalize(string value)
        => normalize == null
            ? value
            : normalize(value) ?? throw new EntityFrameworkEncryptionException($"Blind index normalization of {label} returned null");

    private byte[] GetKey()
        => (keyRing ?? throw new EntityFrameworkEncryptionException(
                $"Encryption is not configured for {contextType.Name}. " +
                "Register it with services.AddEncryption(...) and create the context through dependency injection"))
            .GetIndexKey(contextType, label);
}

internal interface IBlindIndexConverter
{
    BlindIndexer Indexer { get; }

    /// <summary>Encrypted property the blind index belongs to.</summary>
    string SourceProperty { get; }
}

/// <summary>
/// Converter of the shadow <c>{Property}_Index</c> property, which holds the same value as the encrypted property:
/// the value is hashed on save and in query parameters. Hashes can't be reversed, so reading returns the default.
/// </summary>
internal sealed class BlindIndexConverter<TModel>(BlindIndexer indexer, ValueConverter? conversion, string sourceProperty)
    : ValueConverter<TModel, byte[]>(
        x => Compute(indexer, conversion, x),
        x => default!), IBlindIndexConverter
{
    public BlindIndexer Indexer => indexer;

    public string SourceProperty => sourceProperty;

    private static byte[] Compute(BlindIndexer indexer, ValueConverter? conversion, TModel value)
        => indexer.Compute((conversion == null ? value : conversion.ConvertToProvider(value))!);
}
