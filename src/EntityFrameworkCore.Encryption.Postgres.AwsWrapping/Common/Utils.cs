using System.Diagnostics.CodeAnalysis;
using EntityFrameworkCore.Encryption.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Common;

[SuppressMessage("Usage", "EF1001:Internal EF Core API usage.")]
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