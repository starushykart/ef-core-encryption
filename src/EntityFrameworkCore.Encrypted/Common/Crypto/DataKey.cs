using System.Security.Cryptography;

namespace EntityFrameworkCore.Encrypted.Common.Crypto;

/// <summary>
/// Data key with one <see cref="AesGcm"/> instance per thread: creating it sets up the key schedule, which costs
/// more than encrypting a typical value, and an instance can't be shared between threads.
/// </summary>
internal sealed class DataKey : IDisposable
{
    private readonly byte[] _key;
    private readonly ThreadLocal<AesGcm> _ciphers;

    public DataKey(byte[] key)
    {
        _key = key;
        _ciphers = new ThreadLocal<AesGcm>(() => new AesGcm(_key, Envelope.TagSize), trackAllValues: true);
    }

    public AesGcm Cipher => _ciphers.Value!;

    public void Dispose()
    {
        foreach (var cipher in _ciphers.Values)
            cipher.Dispose();

        _ciphers.Dispose();
        CryptographicOperations.ZeroMemory(_key);
    }
}
