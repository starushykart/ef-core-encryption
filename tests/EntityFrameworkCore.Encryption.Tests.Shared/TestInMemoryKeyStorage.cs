using System.Collections.Concurrent;
using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.Common.Exceptions;

namespace EntityFrameworkCore.Encryption.Tests.Shared;

public class TestInMemoryKeyStorage : IKeyStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _keys = [];

    public void AddKey(string contextName, byte[] key)
    {
        if (key == null || key.Length == 0)
            throw new EntityFrameworkEncryptionException("Data key can not be null or empty");

        _keys.TryAdd(contextName, key);
    }

    public byte[] GetKey(string contextName)
        => _keys[contextName];

    public bool ContainsKey(string contextName)
        => _keys.ContainsKey(contextName);

    public void Clear()
        => _keys.Clear();
}