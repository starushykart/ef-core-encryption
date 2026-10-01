using Testcontainers.LocalStack;

namespace EntityFrameworkCore.Encrypted.Tests.AwsKms.Common.Fixtures;

public class LocalstackContainerFixture : IAsyncLifetime
{
    // 4.15+ requires a paid LOCALSTACK_AUTH_TOKEN; 4.14 is the last free community release
    private readonly LocalStackContainer _container = new LocalStackBuilder("localstack/localstack:4.14.0")
        .WithEnvironment("SERVICES", "kms")
        .WithName($"ef_core_encrypted_localstack_{Guid.NewGuid()}")
        .WithCleanUp(true)
        .Build();

    public Guid TestKeyId { get; set; } = Guid.NewGuid();
    public string Url => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await _container.ExecAsync([
            "awslocal",
            "kms",
            "create-key",
            "--tags",
            $"TagKey=_custom_id_,TagValue={TestKeyId}"
        ]);
    }

    public async ValueTask DisposeAsync()
        => await _container.DisposeAsync();
}