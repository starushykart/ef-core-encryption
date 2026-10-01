using EntityFrameworkCore.Encrypted.Common;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Common.Maintenance;
using EntityFrameworkCore.Encrypted.Keys;
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

        foreach (var (contextType, keyRing) in EncryptedContexts.Find(scope.ServiceProvider))
            await keyRing.InitializeAsync(contextType, cancellationToken);
    }

    /// <summary>
    /// Creates a new root key of <typeparamref name="TContext"/> that encrypts new values from now on.
    /// Other instances switch to it as soon as they read a value encrypted with it, or on restart.
    /// </summary>
    /// <returns>Id of the new root key.</returns>
    public static Task<int> RotateRootKeyAsync<TContext>(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        return GetService<DataKeyRing>(serviceProvider).RotateRootKeyAsync(typeof(TContext), cancellationToken);
    }

    /// <summary>
    /// Wraps all root keys of <typeparamref name="TContext"/> with the currently configured wrapping key, e.g. after
    /// moving to another KMS key. Values are not touched. Keep the previous wrapping key enabled until this completes.
    /// </summary>
    /// <returns>Number of rewrapped root keys.</returns>
    public static Task<int> RewrapRootKeysAsync<TContext>(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        where TContext : DbContext
        => GetService<IRootKeyProvider>(serviceProvider).RewrapRootKeysAsync(typeof(TContext), cancellationToken);

    /// <summary>
    /// Counts stored values of <typeparamref name="TContext"/> per column and key, e.g. to check that no value
    /// uses a root key before retiring it. Reads every encrypted column of every table.
    /// </summary>
    public static Task<IReadOnlyList<KeyUsage>> GetKeyUsageAsync<TContext>(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        where TContext : DbContext
        => GetService<EncryptedDataMaintenance>(serviceProvider).GetKeyUsageAsync(typeof(TContext), cancellationToken);

    /// <summary>
    /// Re-encrypts stored values of <typeparamref name="TContext"/> that are not encrypted with the active key
    /// (root key and data key version), <paramref name="batchSize"/> values per transaction.
    /// Safe to run while the application is running and to restart: values changed concurrently are left as is.
    /// </summary>
    public static Task<ReEncryptionResult> ReEncryptAsync<TContext>(
        this IServiceProvider serviceProvider, int batchSize = 1000, CancellationToken cancellationToken = default)
        where TContext : DbContext
        => GetService<EncryptedDataMaintenance>(serviceProvider).ReEncryptAsync(typeof(TContext), batchSize, cancellationToken);

    private static T GetService<T>(IServiceProvider serviceProvider) where T : notnull
        => serviceProvider.GetService<T>()
           ?? throw new EntityFrameworkEncryptionException(
               $"Encryption services are not registered. Call services.{nameof(ServiceCollectionExtensions.AddEncryption)}(...)");
}
