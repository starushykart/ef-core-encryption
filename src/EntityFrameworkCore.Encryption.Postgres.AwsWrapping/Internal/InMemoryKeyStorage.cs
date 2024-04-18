using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Internal;

internal static class InMemoryKeyStorage
{
    public static readonly ConcurrentDictionary<string, EncryptionSpecification> Specifications = [];

    internal static void Register(string contextName, EncryptionType spec)
    {
        if (contextName.Equals(nameof(DbContext)) || Specifications.ContainsKey(contextName))
            return;

        Specifications.TryAdd(contextName, new EncryptionSpecification(spec));
    }
}
