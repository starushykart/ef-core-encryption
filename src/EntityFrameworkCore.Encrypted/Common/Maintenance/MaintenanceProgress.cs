using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EntityFrameworkCore.Encrypted.Common.Maintenance;

/// <summary>Progress of a maintenance operation on one table: logged at most every 10 seconds, and when the table is done.</summary>
internal sealed class MaintenanceProgress(ILogger logger, string operation, string table, long total)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private TimeSpan _logged;

    public long Scanned { get; private set; }
    public long Updated { get; set; }
    public long Skipped { get; set; }
    public long Invalid { get; set; }

    public TimeSpan Elapsed => _watch.Elapsed;

    public void Start()
        => logger.LogInformation("{Operation} {Table}: {Total} rows with encrypted values", operation, table, total);

    public void Row()
    {
        Scanned++;

        if (_watch.Elapsed - _logged < Interval)
            return;

        _logged = _watch.Elapsed;

        // the total is counted before the scan: rows added meanwhile are scanned too
        logger.LogInformation("{Operation} {Table}: {Scanned} of {Total} rows scanned ({Percent:0}%), {Updated} updated so far",
            operation, table, Scanned, total, total == 0 ? 100 : Math.Min(100, Scanned * 100.0 / total), Updated);
    }

    public void Batch(long updated, long skipped)
    {
        Updated += updated;
        Skipped += skipped;
        logger.LogDebug("{Operation} {Table}: batch written, {Updated} updated, {Skipped} changed by the application meanwhile",
            operation, table, updated, skipped);
    }

    public void Finish()
        => logger.LogInformation(
            "{Operation} {Table} finished in {Elapsed}: {Scanned} rows scanned, {Updated} updated, {Skipped} changed by the application meanwhile, {Invalid} invalid",
            operation, table, _watch.Elapsed, Scanned, Updated, Skipped, Invalid);
}
