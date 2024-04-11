using EntityFrameworkCore.Encryption.Metadata;

namespace EntityFrameworkCore.Encryption.Common.Abstractions;

public interface IKeyMetadataStorage<TContext>
{
    KeyMetadata? Get();
    void Create(KeyMetadata metadata);
}