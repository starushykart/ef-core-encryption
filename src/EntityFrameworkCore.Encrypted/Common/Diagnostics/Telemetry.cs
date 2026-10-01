using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace EntityFrameworkCore.Encrypted.Common.Diagnostics;

/// <summary>
/// Activities for key management and maintenance operations (not per value); metrics for values, key management
/// service calls, key loads and re-encryption. Tags: <c>db.context</c> (context type name), <c>operation</c>,
/// <c>trigger</c>, <c>result</c>, <c>error.type</c>.
/// </summary>
internal static class Telemetry
{
    private const string Prefix = "efcore.encryption.";

    private static readonly string? Version = typeof(Telemetry).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    public static readonly ActivitySource Source = new(EncryptionInstrumentation.Name, Version);
    private static readonly Meter Meter = new(EncryptionInstrumentation.Name, Version);

    private static readonly Counter<long> Values = Meter.CreateCounter<long>(
        Prefix + "values", "{value}", "Values encrypted or decrypted");

    private static readonly Counter<long> DecryptionFailures = Meter.CreateCounter<long>(
        Prefix + "decryption.failures", "{value}", "Values that couldn't be decrypted: tampered, root key not found or invalid format");

    private static readonly Histogram<double> KeyWrapperDuration = Meter.CreateHistogram<double>(
        Prefix + "key_wrapper.duration", "s", "Duration of key management service calls (generate, unwrap, rewrap)");

    private static readonly Counter<long> RootKeyLoads = Meter.CreateCounter<long>(
        Prefix + "root_key.loads", "{key}", "Root keys loaded into memory");

    private static readonly Counter<long> ReEncryptedValues = Meter.CreateCounter<long>(
        Prefix + "reencryption.values", "{value}", "Values processed by re-encryption");

    private static readonly Counter<long> LegacyValues = Meter.CreateCounter<long>(
        Prefix + "legacy.values", "{value}", "Values read with the legacy decryptor: not migrated to the library's format yet");

    public static class Operations
    {
        public const string Encrypt = "encrypt";
        public const string Decrypt = "decrypt";
        public const string Generate = "generate";
        public const string Unwrap = "unwrap";
        public const string Rewrap = "rewrap";
    }

    public static Activity? StartActivity(string name, Type contextType)
        => Source.StartActivity(Prefix + name)?.SetTag("db.context", contextType.Name);

    public static void RecordValue(string contextName, string operation)
    {
        if (Values.Enabled)
            Values.Add(1, new KeyValuePair<string, object?>("db.context", contextName), new KeyValuePair<string, object?>("operation", operation));
    }

    public static void RecordLegacyValue(string contextName)
        => LegacyValues.Add(1, new KeyValuePair<string, object?>("db.context", contextName));

    public static void RecordDecryptionFailure(string contextName, string errorType)
        => DecryptionFailures.Add(1, new KeyValuePair<string, object?>("db.context", contextName), new KeyValuePair<string, object?>("error.type", errorType));

    public static void RecordRootKeyLoad(Type contextType, string trigger, Exception? error)
        => RootKeyLoads.Add(1,
            new KeyValuePair<string, object?>("db.context", contextType.Name),
            new KeyValuePair<string, object?>("trigger", trigger),
            new KeyValuePair<string, object?>("result", error == null ? "success" : "failure"));

    public static void RecordReEncryption(Type contextType, string result, long count)
    {
        if (count > 0)
            ReEncryptedValues.Add(count, new KeyValuePair<string, object?>("db.context", contextType.Name), new KeyValuePair<string, object?>("result", result));
    }

    /// <summary>Traces and times a key management service call.</summary>
    public static async Task<T> KeyWrapperAsync<T>(string operation, Type contextType, int rootKeyId, Func<Task<T>> call)
    {
        using var activity = StartActivity("key_wrapper." + operation, contextType)?.SetTag("root_key.id", rootKeyId);
        var started = Stopwatch.GetTimestamp();
        string? errorType = null;

        try
        {
            return await call();
        }
        catch (Exception ex)
        {
            errorType = ex.GetType().FullName;
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message).AddException(ex);
            throw;
        }
        finally
        {
            var tags = new TagList
            {
                { "db.context", contextType.Name },
                { "operation", operation }
            };

            if (errorType != null)
                tags.Add("error.type", errorType);

            KeyWrapperDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }

    public static void SetError(this Activity? activity, Exception ex)
        => activity?.SetStatus(ActivityStatusCode.Error, ex.Message).AddException(ex);
}
