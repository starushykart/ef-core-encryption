using System.Collections.Concurrent;
using System.Security.Cryptography;
using EntityFrameworkCore.Encrypted.Keys;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;

/// <summary>
/// Key management service stand-in: wraps root keys with AES-GCM under master keys and counts calls.
/// New root keys are wrapped with the current master key; any enabled master key unwraps.
/// </summary>
public sealed class InMemoryKeyWrapper : IKeyWrapper
{
    public const string WrappingKeyId = "test-master-key";

    private readonly ConcurrentDictionary<string, byte[]> _masterKeys = new() { [WrappingKeyId] = RandomNumberGenerator.GetBytes(32) };
    private readonly ConcurrentDictionary<string, bool> _disabled = new();
    private int _generateCalls;
    private int _unwrapCalls;
    private int _rewrapCalls;

    public int GenerateCalls => _generateCalls;
    public int UnwrapCalls => _unwrapCalls;
    public int RewrapCalls => _rewrapCalls;

    /// <summary>Master key used for new and rewrapped root keys.</summary>
    public string CurrentWrappingKeyId { get; private set; } = WrappingKeyId;

    /// <summary>Simulated key management service latency, so concurrent callers overlap.</summary>
    public TimeSpan UnwrapDelay { get; set; }

    /// <summary>Creates a master key and uses it for new and rewrapped root keys, like moving to another KMS key.</summary>
    public void UseNewMasterKey(string wrappingKeyId)
    {
        _masterKeys[wrappingKeyId] = RandomNumberGenerator.GetBytes(32);
        CurrentWrappingKeyId = wrappingKeyId;
    }

    public void Disable(string wrappingKeyId)
        => _disabled[wrappingKeyId] = true;

    public Task<GeneratedRootKey> GenerateAsync(int rootKeyId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _generateCalls);

        var key = RandomNumberGenerator.GetBytes(32);
        return Task.FromResult(new GeneratedRootKey((byte[])key.Clone(), CurrentWrappingKeyId, Wrap(CurrentWrappingKeyId, key, rootKeyId)));
    }

    public async Task<byte[]> UnwrapAsync(WrappedRootKey rootKey, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _unwrapCalls);
        await Task.Delay(UnwrapDelay, cancellationToken);

        return Unwrap(rootKey);
    }

    public Task<WrappedRootKey> RewrapAsync(WrappedRootKey rootKey, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _rewrapCalls);

        var key = Unwrap(rootKey);
        return Task.FromResult(rootKey with { WrappingKeyId = CurrentWrappingKeyId, WrappedKey = Wrap(CurrentWrappingKeyId, key, rootKey.Id) });
    }

    private byte[] Unwrap(WrappedRootKey rootKey)
    {
        var masterKey = GetMasterKey(rootKey.WrappingKeyId);
        var nonce = rootKey.WrappedKey[..12];
        var tag = rootKey.WrappedKey[^16..];
        var key = new byte[rootKey.WrappedKey.Length - 28];

        using var aes = new AesGcm(masterKey, 16);
        aes.Decrypt(nonce, rootKey.WrappedKey[12..^16], tag, key, BitConverter.GetBytes(rootKey.Id));
        return key;
    }

    private byte[] Wrap(string wrappingKeyId, byte[] key, int rootKeyId)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[key.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(GetMasterKey(wrappingKeyId), 16);
        aes.Encrypt(nonce, key, ciphertext, tag, BitConverter.GetBytes(rootKeyId));
        return [..nonce, ..ciphertext, ..tag];
    }

    private byte[] GetMasterKey(string wrappingKeyId)
    {
        if (_disabled.ContainsKey(wrappingKeyId))
            throw new InvalidOperationException($"Master key {wrappingKeyId} is disabled");

        return _masterKeys.TryGetValue(wrappingKeyId, out var masterKey)
            ? masterKey
            : throw new InvalidOperationException($"Master key {wrappingKeyId} not found");
    }
}
