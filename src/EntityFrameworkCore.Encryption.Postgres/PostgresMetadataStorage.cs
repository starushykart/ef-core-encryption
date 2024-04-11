using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.Common.Exceptions;
using EntityFrameworkCore.Encryption.Metadata;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Postgres;

public class PostgresMetadataStorage<TContext>(IDbContextFactory<MetadataContext> factory) : IKeyMetadataStorage<TContext>
{
    private static readonly string ContextId = typeof(TContext).Name;
    public KeyMetadata? Get()
    {
        using var context = factory.CreateDbContext();

        return context.Metadata
            .FirstOrDefault(x => x.ContextId == ContextId);
    }

    public void Create(KeyMetadata metadata)
    {
        if (metadata.ContextId != ContextId)
            throw new EntityFrameworkEncryptionException("KeyMetadata for another context cannot be stored");
        
        using var context = factory.CreateDbContext();
        context.Metadata.Add(metadata);
        
        context.SaveChanges();
    }
}