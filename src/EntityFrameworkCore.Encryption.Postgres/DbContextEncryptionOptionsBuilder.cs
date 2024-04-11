using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.EncryptionOptions;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encryption.Postgres;

public static class DbContextEncryptionOptionsBuilderExtensions
{
    public static DbContextEncryptionOptionsBuilder UseCurrentDatabaseStorage(
        this DbContextEncryptionOptionsBuilder builder)
    {
        var builderConfigure = Utils.GetCopyWithOnlyDbProvider(builder.SourceContextOptionsAction);

        builder.ConfigureKeyMetadataStorage(x =>
        {
            x.AddDbContextFactory<MetadataContext>(builderConfigure);

            x.AddSingleton(
                typeof(IKeyMetadataStorage<>).MakeGenericType(builder.SourceContextType),
                typeof(PostgresMetadataStorage<>).MakeGenericType(builder.SourceContextType));
        });
        
        return builder;
    }
}