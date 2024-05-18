using System.Diagnostics.CodeAnalysis;
using EntityFrameworkCore.Encrypted.Common;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;

namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common;

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

    internal static IEnumerable<(string ContextName, EncryptionType EncryptionType)> GetEncryptionInfo(this IServiceProvider provider)
        => provider
            .GetServices<DbContextOptions>()
            .Where(x => x.FindExtension<EncryptionDbContextOptionsExtension>() != null)
            .Select(x =>
            (
                x.ContextType.Name,
                x.GetExtension<EncryptionDbContextOptionsExtension>().EncryptionType
            ));
}