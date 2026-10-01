using EntityFrameworkCore.Encrypted.Common.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EntityFrameworkCore.Encrypted.Common.Hosting;

/// <summary>
/// Healthy when the keys of every encrypted context are loaded. Keys that aren't loaded yet are loaded by the check,
/// so after the first success it costs no key store or key management service calls.
/// </summary>
internal sealed class EncryptionKeysHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var data = new Dictionary<string, object>();
        var failures = new List<string>();
        Exception? exception = null;

        foreach (var (contextType, keyRing) in EncryptedContexts.Find(scope.ServiceProvider))
        {
            try
            {
                data[contextType.Name] = (await keyRing.LoadAsync(contextType, cancellationToken)).ToString();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failures.Add(contextType.Name);
                data[contextType.Name] = ex.Message;
                exception ??= ex;
            }
        }

        if (data.Count == 0)
            return new HealthCheckResult(context.Registration.FailureStatus,
                "No registered context uses encryption", new EntityFrameworkEncryptionException("No registered context uses encryption"));

        return failures.Count == 0
            ? HealthCheckResult.Healthy("Encryption keys are loaded", data)
            : new HealthCheckResult(context.Registration.FailureStatus,
                $"Encryption keys of {string.Join(", ", failures)} can't be loaded", exception, data);
    }
}
