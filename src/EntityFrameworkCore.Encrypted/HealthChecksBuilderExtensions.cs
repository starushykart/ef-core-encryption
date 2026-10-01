using EntityFrameworkCore.Encrypted.Common.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EntityFrameworkCore.Encrypted;

/// <summary>Health checks for encryption keys.</summary>
public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// Checks that the keys of every encrypted context are loaded, loading them if needed
    /// (key store and key management service reachable, store migrated).
    /// </summary>
    public static IHealthChecksBuilder AddEncryptionKeys(
        this IHealthChecksBuilder builder,
        string name = "encryption-keys",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => ActivatorUtilities.CreateInstance<EncryptionKeysHealthCheck>(sp),
            failureStatus,
            tags,
            timeout));
    }
}
