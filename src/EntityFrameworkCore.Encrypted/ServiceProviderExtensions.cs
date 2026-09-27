using EntityFrameworkCore.Encrypted.Common;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encrypted;

public static class ServiceProviderExtensions
{
    /// <summary>
    /// Loads the active root key of all registered contexts that use encryption.
    /// Runs automatically on host start; call it explicitly in apps without a generic host.
    /// </summary>
    public static async Task InitializeEncryptionAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();

        var encrypted = scope.ServiceProvider
            .GetServices<DbContextOptions>()
            .Select(options => (options.ContextType, options.FindExtension<EncryptionDbContextOptionsExtension>()?.KeyRing))
            .Where(x => x.KeyRing != null)
            .DistinctBy(x => x.ContextType);

        foreach (var (contextType, keyRing) in encrypted)
            await keyRing!.InitializeAsync(contextType, cancellationToken);
    }

    /// <summary>
    /// Creates a new root key of <typeparamref name="TContext"/> that encrypts new values from now on.
    /// Other instances switch to it as soon as they read a value encrypted with it, or on restart.
    /// </summary>
    /// <returns>Id of the new root key.</returns>
    public static Task<int> RotateRootKeyAsync<TContext>(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        var keyRing = serviceProvider.GetService<DataKeyRing>()
            ?? throw new EntityFrameworkEncryptionException(
                $"Encryption services are not registered. Call services.{nameof(ServiceCollectionExtensions.AddEncryption)}(...)");

        return keyRing.RotateRootKeyAsync(typeof(TContext), cancellationToken);
    }
}
