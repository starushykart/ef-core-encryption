using Microsoft.Extensions.Hosting;

namespace EntityFrameworkCore.Encrypted.Common.Hosting;

/// <summary>
/// Loads data keys in <see cref="IHostedLifecycleService.StartingAsync"/>, which runs before
/// <see cref="IHostedService.StartAsync"/> of any hosted service (migrations, background workers).
/// </summary>
internal sealed class EncryptionKeyInitializer(IServiceProvider serviceProvider) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
        => serviceProvider.InitializeEncryptionAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
