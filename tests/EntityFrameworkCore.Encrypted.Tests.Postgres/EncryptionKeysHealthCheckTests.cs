using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

public class EncryptionKeysHealthCheckTests
{
    private readonly InMemoryKeyWrapper _wrapper = new();
    private readonly InMemoryRootKeyStore _store = new();

    [Fact]
    public async Task Should_be_healthy_when_keys_are_loaded()
    {
        await using var provider = Build();
        await provider.InitializeEncryptionAsync();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries["encryption-keys"].Data.Should().Contain(nameof(DocumentDbContext), "root 1, v0");
    }

    [Fact]
    public async Task Should_load_keys_that_are_not_loaded_yet()
    {
        await using var provider = Build();

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Status.Should().Be(HealthStatus.Healthy);
        _wrapper.GenerateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Should_be_unhealthy_until_keys_can_be_loaded()
    {
        _store.Unavailable = true;
        await using var provider = Build();
        var healthChecks = provider.GetRequiredService<HealthCheckService>();

        var unavailable = await healthChecks.CheckHealthAsync();
        _store.Unavailable = false;
        var available = await healthChecks.CheckHealthAsync();

        unavailable.Status.Should().Be(HealthStatus.Unhealthy);
        unavailable.Entries["encryption-keys"].Description.Should().Contain(nameof(DocumentDbContext));
        available.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Should_use_configured_failure_status()
    {
        _store.Unavailable = true;
        await using var provider = Build(failureStatus: HealthStatus.Degraded);

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Status.Should().Be(HealthStatus.Degraded);
    }

    private ServiceProvider Build(HealthStatus? failureStatus = null)
        => DocumentDbContext.BuildProvider(
            x => x.UseKeyWrapper(_ => _wrapper).UseRootKeyStore(_ => _store),
            services => services.AddHealthChecks().AddEncryptionKeys(failureStatus: failureStatus));
}
