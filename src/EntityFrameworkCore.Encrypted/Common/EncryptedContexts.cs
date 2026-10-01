using EntityFrameworkCore.Encrypted.Common.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encrypted.Common;

internal static class EncryptedContexts
{
    /// <summary>Registered contexts that use encryption, with their key ring.</summary>
    /// <param name="serviceProvider">Scoped provider: context options are scoped services.</param>
    public static IEnumerable<(Type ContextType, DataKeyRing KeyRing)> Find(IServiceProvider serviceProvider)
        => serviceProvider
            .GetServices<DbContextOptions>()
            .Select(options => (options.ContextType, KeyRing: options.FindExtension<EncryptionDbContextOptionsExtension>()?.KeyRing))
            .Where(x => x.KeyRing != null)
            .DistinctBy(x => x.ContextType)
            .Select(x => (x.ContextType, x.KeyRing!));
}
