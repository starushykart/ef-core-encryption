namespace EntityFrameworkCore.Encrypted.Keys;

/// <summary>Generates and unwraps root keys using a key management service (envelope encryption).</summary>
public interface IKeyWrapper
{
    Task<GeneratedRootKey> GenerateAsync(int rootKeyId, CancellationToken cancellationToken);

    Task<byte[]> UnwrapAsync(WrappedRootKey rootKey, CancellationToken cancellationToken);
}
