using EntityFrameworkCore.Encryption.Common.Abstractions;

namespace EntityFrameworkCore.Encryption.Internal.Providers;

public class ConstantKeyProvider(string keyBase64) : IKeyProvider
{
    private readonly byte[] _key = Convert.FromBase64String(keyBase64);

    public byte[] GetDataKey()
        => _key;
}