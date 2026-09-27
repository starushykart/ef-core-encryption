using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Plugin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;

/// <summary>Model-only context for unit tests: nothing is sent to the database.</summary>
public sealed class DocumentDbContext(DbContextOptions<DocumentDbContext> options) : DbContext(options)
{
    public const string ConnectionString = "Host=localhost";

    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Document>(e =>
        {
            e.ToTable("Documents");
            e.Property(x => x.Text).IsEncrypted();
            e.Property(x => x.Blob).IsEncrypted();
            e.Property(x => x.Other).IsEncrypted();
            e.Property(x => x.RenamedColumn).IsEncrypted("Documents.OldName").HasColumnName("NewName");
        });

    public static ServiceProvider BuildProvider(Action<EncryptionBuilder> configure, Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection()
            .AddEncryption(configure)
            .AddDbContext<DocumentDbContext>(x => x.UseNpgsql(ConnectionString).UseEncryption());

        services?.Invoke(collection);
        return collection.BuildServiceProvider();
    }

    public static ValueConverter GetConverter(IServiceProvider provider, string property)
    {
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DocumentDbContext>().GetConverter(property);
    }

    public ValueConverter GetConverter(string property)
        => Model.FindEntityType(typeof(Document))!.FindProperty(property)!.GetValueConverter()!;

    public string GetLabel(string property)
        => ((IEncryptionConverter)GetConverter(property)).Encryptor.Label;
}

public sealed class Document
{
    public int Id { get; set; }
    public string? Text { get; set; }
    public byte[]? Blob { get; set; }
    public string? Other { get; set; }
    public string? RenamedColumn { get; set; }

    [Encrypted(Label = "shared")]
    public string? SharedA { get; set; }

    [Encrypted(Label = "shared")]
    public string? SharedB { get; set; }
}
