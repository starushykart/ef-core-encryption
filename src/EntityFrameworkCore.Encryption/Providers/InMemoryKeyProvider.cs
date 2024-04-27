using EntityFrameworkCore.Encryption.Common.Abstractions;

namespace EntityFrameworkCore.Encryption.Providers;

public class InMemoryKeyProvider(IKeyStorage storage, string contextId) : IKeyProvider
{
    public byte[] GetKey()
        => storage.GetKey(contextId);
}