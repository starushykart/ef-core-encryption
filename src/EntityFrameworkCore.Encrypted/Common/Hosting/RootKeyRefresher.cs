using EntityFrameworkCore.Encrypted.Common.Keys;
using Microsoft.Extensions.Hosting;

namespace EntityFrameworkCore.Encrypted.Common.Hosting;

/// <summary>Periodically switches to root keys rotated by other instances; disabled unless an interval is configured.</summary>
internal sealed class RootKeyRefresher(DataKeyRing keyRing, EncryptionSettings settings, TimeProvider timeProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (settings.RootKeyRefreshInterval is not { } interval)
            return;

        using var timer = new PeriodicTimer(interval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await keyRing.RefreshAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        { }
    }
}
