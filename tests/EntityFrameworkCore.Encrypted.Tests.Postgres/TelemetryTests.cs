using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

/// <summary>Measurements are filtered by the context of these tests: the meter and activity source are global.</summary>
public sealed class TelemetryTests : IDisposable
{
    private readonly InMemoryKeyWrapper _wrapper = new();
    private readonly InMemoryRootKeyStore _store = new();
    private readonly ConcurrentQueue<(string Name, double Value, Dictionary<string, object?> Tags)> _measurements = new();
    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly MeterListener _meterListener = new();
    private readonly ActivityListener _activityListener;

    public TelemetryTests()
    {
        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == EncryptionInstrumentation.Name)
                listener.EnableMeasurementEvents(instrument);
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meterListener.Start();

        _activityListener = new ActivityListener
        {
            ShouldListenTo = x => x.Name == EncryptionInstrumentation.Name,
            Sample = (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = x =>
            {
                if (x.GetTagItem("db.context") as string == nameof(TelemetryDbContext))
                    _activities.Enqueue(x);
            }
        };
        ActivitySource.AddActivityListener(_activityListener);
    }

    [Fact]
    public async Task Should_count_encrypted_and_decrypted_values()
    {
        await using var provider = Build();
        var converter = GetConverter(provider);

        var encrypted = converter.ConvertToProvider("secret");
        converter.ConvertFromProvider(encrypted);
        converter.ConvertFromProvider(encrypted);

        Sum("efcore.encryption.values", "operation", "encrypt").Should().Be(1);
        Sum("efcore.encryption.values", "operation", "decrypt").Should().Be(2);
    }

    [Fact]
    public async Task Should_count_decryption_failures_by_reason()
    {
        await using var provider = Build();
        var converter = GetConverter(provider);
        var tampered = Convert.FromBase64String((string)converter.ConvertToProvider("secret")!);
        tampered[^1] ^= 1;

        var act = () => converter.ConvertFromProvider(Convert.ToBase64String(tampered));
        var invalid = () => converter.ConvertFromProvider(Convert.ToBase64String(new byte[40]));

        act.Should().Throw<EntityFrameworkEncryptionException>();
        invalid.Should().Throw<EntityFrameworkEncryptionException>();
        Sum("efcore.encryption.decryption.failures", "error.type", "tampered").Should().Be(1);
        Sum("efcore.encryption.decryption.failures", "error.type", "invalid_format").Should().Be(1);
    }

    [Fact]
    public async Task Should_trace_and_time_key_management_calls_and_key_loads()
    {
        await using (var first = Build())
            await first.InitializeEncryptionAsync();

        await using var restarted = Build();
        await restarted.InitializeEncryptionAsync();

        Count("efcore.encryption.key_wrapper.duration", "operation", "generate").Should().Be(1);
        Count("efcore.encryption.key_wrapper.duration", "operation", "unwrap").Should().Be(1);
        Sum("efcore.encryption.root_key.loads", "trigger", "active").Should().Be(2);
        _activities.Select(x => x.OperationName).Should().Contain(
            ["efcore.encryption.key_wrapper.generate", "efcore.encryption.key_wrapper.unwrap", "efcore.encryption.root_key.load"]);
        _activities.Single(x => x.OperationName == "efcore.encryption.key_wrapper.unwrap").GetTagItem("root_key.id").Should().Be(1);
    }

    [Fact]
    public async Task Should_trace_rotation_and_on_demand_loads()
    {
        await using var instanceA = Build();
        await using var instanceB = Build();
        await instanceB.InitializeEncryptionAsync();

        await instanceA.RotateRootKeyAsync<TelemetryDbContext>();
        GetConverter(instanceB).ConvertFromProvider(GetConverter(instanceA).ConvertToProvider("secret"));

        Sum("efcore.encryption.root_key.loads", "trigger", "on_demand").Should().Be(1);
        _activities.Should().Contain(x => x.OperationName == "efcore.encryption.root_key.rotate" && Equals(x.GetTagItem("root_key.id"), 2));
    }

    [Fact]
    public async Task Should_record_failed_key_management_calls()
    {
        await using (var first = Build())
            await first.InitializeEncryptionAsync();

        _wrapper.Disable(InMemoryKeyWrapper.WrappingKeyId);
        await using var restarted = Build();

        var act = () => restarted.InitializeEncryptionAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        _measurements.Should().Contain(x => x.Name == "efcore.encryption.key_wrapper.duration"
                                            && Equals(x.Tags["operation"], "unwrap")
                                            && Equals(x.Tags.GetValueOrDefault("error.type"), typeof(InvalidOperationException).FullName));
        Sum("efcore.encryption.root_key.loads", "result", "failure").Should().Be(1);
        _activities.Should().Contain(x => x.OperationName == "efcore.encryption.key_wrapper.unwrap" && x.Status == ActivityStatusCode.Error);
    }

    public void Dispose()
    {
        _meterListener.Dispose();
        _activityListener.Dispose();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>();
        foreach (var tag in tags)
            dictionary[tag.Key] = tag.Value;

        if (dictionary.GetValueOrDefault("db.context") as string == nameof(TelemetryDbContext))
            _measurements.Enqueue((instrument.Name, value, dictionary));
    }

    private double Sum(string name, string tag, string value)
        => _measurements.Where(x => x.Name == name && Equals(x.Tags.GetValueOrDefault(tag), value)).Sum(x => x.Value);

    private int Count(string name, string tag, string value)
        => _measurements.Count(x => x.Name == name && Equals(x.Tags.GetValueOrDefault(tag), value));

    private ServiceProvider Build()
        => new ServiceCollection()
            .AddEncryption(x => x.UseKeyWrapper(_ => _wrapper).UseRootKeyStore(_ => _store))
            .AddDbContext<TelemetryDbContext>(x => x.UseNpgsql("Host=localhost").UseEncryption())
            .BuildServiceProvider();

    private static ValueConverter GetConverter(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TelemetryDbContext>().Model
            .FindEntityType(typeof(TelemetryEntity))!.FindProperty(nameof(TelemetryEntity.Secret))!.GetValueConverter()!;
    }

    public sealed class TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : DbContext(options)
    {
        public DbSet<TelemetryEntity> Entities => Set<TelemetryEntity>();
    }

    public sealed class TelemetryEntity
    {
        public int Id { get; set; }

        [Encrypted]
        public string? Secret { get; set; }
    }
}
