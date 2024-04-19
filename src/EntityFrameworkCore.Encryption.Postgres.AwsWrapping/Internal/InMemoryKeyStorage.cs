using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Internal;

internal static class InMemoryKeyStorage
{
    private static readonly ConcurrentDictionary<string, EncryptionSpecification> Specifications = [];
    
    internal static IReadOnlyDictionary<string, EncryptionSpecification> GetRegisteredSpecifications()
        => Specifications;
    
    internal static byte[] GetKey(string contextName)
        => Specifications[contextName].Key;
    
    internal static void Register(string contextName, EncryptionType spec)
    {
        if (contextName.Equals(nameof(DbContext)) || Specifications.ContainsKey(contextName))
            return;

        Specifications.TryAdd(contextName, new EncryptionSpecification(spec));
    }
}
