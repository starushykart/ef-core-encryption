using EntityFrameworkCore.Encrypted.Common.Abstractions;

namespace EntityFrameworkCore.Encrypted.Common.Keys;

internal sealed class ConstantDataKeySource(byte[] key) : IDataKeySource
{
    public ValueTask<byte[]> GetDataKeyAsync(DataKeyContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(key);
}
