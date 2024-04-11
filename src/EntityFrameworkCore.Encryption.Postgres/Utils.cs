using EntityFrameworkCore.Encryption.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Encryption.Postgres;

internal static class Utils
{
    internal const string DefaultEncryptionSchema = "encrypt";

    internal static Action<DbContextOptionsBuilder> GetCopyWithOnlyDbProvider(Action<DbContextOptionsBuilder> sourceOptionsAction)
    {
        var sourceBuilder = new DbContextOptionsBuilder();
        sourceOptionsAction(sourceBuilder);

        var originalProvider = sourceBuilder
            .Options
            .Extensions
            .FirstOrDefault(x => x.Info.IsDatabaseProvider);

        if (originalProvider == null)
            throw new EntityFrameworkEncryptionException("Database provider cannot be found to setup metadata context");

        return builder =>
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(originalProvider);
    }
}