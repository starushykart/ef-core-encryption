namespace EntityFrameworkCore.Encrypted;

/// <summary>
/// Traces and metrics, as <see cref="System.Diagnostics.ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/>
/// named <see cref="Name"/>. With OpenTelemetry:
/// <code>
/// .WithTracing(x => x.AddSource(EncryptionInstrumentation.Name))
/// .WithMetrics(x => x.AddMeter(EncryptionInstrumentation.Name))
/// </code>
/// </summary>
public static class EncryptionInstrumentation
{
    public const string Name = "EntityFrameworkCore.Encrypted";
}
