using EntityFrameworkCore.Encrypted.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encrypted;

public static class ServiceProviderExtensions
{
    /// <summary>
    /// Loads data keys of all registered contexts that use encryption.
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
}
