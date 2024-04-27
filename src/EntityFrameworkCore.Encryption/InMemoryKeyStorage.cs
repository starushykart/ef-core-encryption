using System.Collections.Concurrent;
using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.Common.Exceptions;

namespace EntityFrameworkCore.Encryption;

public class InMemoryKeyStorage : IKeyStorage
{
    private static readonly ConcurrentDictionary<string, byte[]> Keys = [];

    public static readonly IKeyStorage Instance = new InMemoryKeyStorage();

    private InMemoryKeyStorage()
    { }

    public void AddKey(string contextName, byte[] key)
    {
        if (key == null || key.Length == 0)
            throw new EntityFrameworkEncryptionException("Data key can not be null or empty");

        Keys.TryAdd(contextName, key);
    }

    public byte[] GetKey(string contextName)
        => Keys[contextName];

    public bool ContainsKey(string contextName)
        => Keys.ContainsKey(contextName);
}
