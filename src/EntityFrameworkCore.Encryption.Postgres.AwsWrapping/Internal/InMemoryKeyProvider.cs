using EntityFrameworkCore.Encryption.Common.Abstractions;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Internal;

public class InMemoryKeyProvider(string contextId) : IKeyProvider
{
    public byte[] GetDataKey()
        => InMemoryKeyStorage.Specifications[contextId].Key;
}