using Testcontainers.LocalStack;

namespace EntityFrameworkCore.Encryption.IntegrationTests.Fixtures;

public class LocalstackContainerFixture : IAsyncLifetime
{
    private readonly LocalStackContainer _container = new LocalStackBuilder()
        .WithEnvironment("SERVICES", "kms")
        .WithName($"db-encryption_localstack_{Guid.NewGuid()}")
        .WithCleanUp(true)
        .Build();

    public Guid TestKeyId { get; set; } = Guid.NewGuid();
    public string Url => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await _container.ExecAsync([
            "awslocal",
            "kms",
            "create-key",
            "--tags",
            $"'[{{\"TagKey\":\"_custom_id_\",\"TagValue\":\"{TestKeyId}\"}}]'"
        ]);
    }

    public async Task DisposeAsync()
        => await _container.DisposeAsync();
}