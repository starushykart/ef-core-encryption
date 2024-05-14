using EntityFrameworkCore.Encrypted.Common.Abstractions;

namespace EntityFrameworkCore.Encrypted.Providers;

public class InMemoryKeyProvider(IKeyStorage storage, string contextId) : IKeyProvider
{
    public byte[] GetKey()
        => storage.GetKey(contextId);
}