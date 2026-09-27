using System.Security.Cryptography;
using EntityFrameworkCore.Encrypted.Keys;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;

/// <summary>Key management service stand-in: wraps root keys with AES-GCM under a master key and counts calls.</summary>
public sealed class InMemoryKeyWrapper : IKeyWrapper
{
    public const string WrappingKeyId = "test-master-key";

    private readonly byte[] _masterKey = RandomNumberGenerator.GetBytes(32);
    private int _generateCalls;
    private int _unwrapCalls;

    public int GenerateCalls => _generateCalls;
    public int UnwrapCalls => _unwrapCalls;

    /// <summary>Simulated key management service latency, so concurrent callers overlap.</summary>
    public TimeSpan UnwrapDelay { get; set; }

    public Task<GeneratedRootKey> GenerateAsync(int rootKeyId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _generateCalls);

        var key = RandomNumberGenerator.GetBytes(32);
        return Task.FromResult(new GeneratedRootKey((byte[])key.Clone(), WrappingKeyId, Wrap(key, rootKeyId)));
    }

    public async Task<byte[]> UnwrapAsync(WrappedRootKey rootKey, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _unwrapCalls);
        await Task.Delay(UnwrapDelay, cancellationToken);

        var nonce = rootKey.WrappedKey[..12];
        var tag = rootKey.WrappedKey[^16..];
        var key = new byte[rootKey.WrappedKey.Length - 28];

        using var aes = new AesGcm(_masterKey, 16);
        aes.Decrypt(nonce, rootKey.WrappedKey[12..^16], tag, key, BitConverter.GetBytes(rootKey.Id));
        return key;
    }

    private byte[] Wrap(byte[] key, int rootKeyId)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[key.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_masterKey, 16);
        aes.Encrypt(nonce, key, ciphertext, tag, BitConverter.GetBytes(rootKeyId));
        return [..nonce, ..ciphertext, ..tag];
    }
}
