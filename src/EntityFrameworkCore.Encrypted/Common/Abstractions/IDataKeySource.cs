namespace EntityFrameworkCore.Encrypted.Common.Abstractions;

/// <summary>
/// Supplies the plaintext data encryption key for a <see cref="Microsoft.EntityFrameworkCore.DbContext"/>.
/// Called once per context type; the result is cached for the lifetime of the application.
/// </summary>
public interface IDataKeySource
{
    ValueTask<byte[]> GetDataKeyAsync(DataKeyContext context, CancellationToken cancellationToken);
}

public sealed record DataKeyContext(Type DbContextType);
