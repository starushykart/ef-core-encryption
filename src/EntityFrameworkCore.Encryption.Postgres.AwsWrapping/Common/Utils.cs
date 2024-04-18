using EntityFrameworkCore.Encryption.Common;
using EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Common;

internal static class Utils
{
    internal static string GetConnectionString(Action<DbContextOptionsBuilder> sourceOptionsAction)
    {
        var sourceBuilder = new DbContextOptionsBuilder();
        sourceOptionsAction(sourceBuilder);

        var connectionString = sourceBuilder
            .Options
            .GetExtension<NpgsqlOptionsExtension>().ConnectionString;

        if (string.IsNullOrEmpty(connectionString))
            throw new EntityFrameworkEncryptionException("Cannot extract connection string from source db context");

        return connectionString;
    }
}