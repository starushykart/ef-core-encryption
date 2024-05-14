using EntityFrameworkCore.Encrypted.Common.Abstractions;

namespace EntityFrameworkCore.Encrypted.Providers;

public class ConstantKeyProvider(string keyBase64) : IKeyProvider
{
    private readonly byte[] _key = Convert.FromBase64String(keyBase64);

    public byte[] GetKey()
        => _key;
}