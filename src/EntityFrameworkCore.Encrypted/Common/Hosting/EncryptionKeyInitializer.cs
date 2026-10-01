using Microsoft.Extensions.Hosting;

namespace EntityFrameworkCore.Encrypted.Common.Hosting;

/// <summary>
/// Loads data keys on startup. Hosted services start in registration order: services registered before
/// <c>AddEncryption</c> (e.g. migrations) run first, services registered after it find the keys loaded.
/// </summary>
internal sealed class EncryptionKeyInitializer(IServiceProvider serviceProvider) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => serviceProvider.InitializeEncryptionAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
