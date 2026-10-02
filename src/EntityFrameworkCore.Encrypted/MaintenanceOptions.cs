namespace EntityFrameworkCore.Encrypted;

/// <summary>How <c>ReEncryptAsync</c> and <c>RebuildBlindIndexesAsync</c> write: batch size and throttling.</summary>
public sealed record MaintenanceOptions
{
    /// <summary>Values written per transaction (default: 1000). Smaller batches hold row locks for a shorter time.</summary>
    public int BatchSize { get; init; } = 1000;

    /// <summary>
    /// Pause after each batch (default: none), to limit the load on the database while the application is serving traffic.
    /// </summary>
    public TimeSpan BatchDelay { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(BatchSize, 1, nameof(BatchSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(BatchDelay, TimeSpan.Zero, nameof(BatchDelay));
    }
}
