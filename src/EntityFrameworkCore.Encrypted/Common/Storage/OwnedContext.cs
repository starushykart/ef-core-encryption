using EntityFrameworkCore.Encrypted.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encrypted.Common.Storage;

/// <summary>Context resolved from dependency injection, disposed only when created from a factory.</summary>
internal readonly struct OwnedContext(DbContext context, bool dispose) : IAsyncDisposable
{
    public DbContext Context { get; } = context;

    public static OwnedContext Create(IServiceProvider serviceProvider, Type dbContextType)
    {
        // AddDbContext / AddDbContextPool / AddDbContextFactory register the context as a scoped service
        if (serviceProvider.GetService(dbContextType) is DbContext scoped)
            return new OwnedContext(scoped, dispose: false);

        // AddPooledDbContextFactory registers only the factory
        var factoryType = typeof(IDbContextFactory<>).MakeGenericType(dbContextType);
        var factory = serviceProvider.GetService(factoryType)
            ?? throw new EntityFrameworkEncryptionException(
                $"{dbContextType.Name} is not registered in dependency injection");

        var context = (DbContext)factoryType.GetMethod(nameof(IDbContextFactory<DbContext>.CreateDbContext))!.Invoke(factory, null)!;
        return new OwnedContext(context, dispose: true);
    }

    public ValueTask DisposeAsync()
        => dispose ? Context.DisposeAsync() : ValueTask.CompletedTask;
}
