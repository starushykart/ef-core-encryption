using Testcontainers.LocalStack;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common.Fixtures;

public class LocalstackContainerFixture : IAsyncLifetime
{
    private readonly LocalStackContainer _container = new LocalStackBuilder()
        .WithImage("localstack/localstack:latest")
        .WithEnvironment("SERVICES", "kms")
        .WithName($"ef_core_encrypted_localstack_{Guid.NewGuid()}")
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
            $"TagKey=_custom_id_,TagValue={TestKeyId}"
        ]);
    }

    public async Task DisposeAsync()
        => await _container.DisposeAsync();
}