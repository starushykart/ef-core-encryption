using Amazon.KeyManagementService;
using Amazon.Runtime;
using EntityFrameworkCore.Encrypted.IntegrationTests.Common;
using EntityFrameworkCore.Encrypted.IntegrationTests.Fixtures;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;
using EntityFrameworkCore.Encrypted.Providers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.IntegrationTests;

public class AwsWrappingHostedServiceTests(
    LocalstackContainerFixture localstack,
    PostgresContainerFixture postgres,
    ITestOutputHelper helper) :
    BaseDatabaseTest(postgres),
    IClassFixture<LocalstackContainerFixture>
{
    private const string TestContextName = nameof(TestDbContext);
    
    [Fact]
    public async Task Should_generate_and_save_new_data_key()
    {
        var wrappingService = Provider.GetServices<IHostedService>().Single();
        var metadataContextFactory = Provider.GetRequiredService<IDbContextFactory<EncryptionMetadataContext>>();

        await wrappingService.StartAsync(CancellationToken.None);
        
        await using var context = await metadataContextFactory.CreateDbContextAsync();
        var metadata = await context.Metadata
            .SingleOrDefaultAsync(x => x.ContextId == TestContextName);

        metadata.Should().NotBeNull();
        metadata!.ContextId.Should().Be(TestContextName);
        metadata!.Key.Should().NotBeEmpty();

        InMemoryKeyStorage.Instance.ContainsKey(TestContextName).Should().BeTrue();
        InMemoryKeyStorage.Instance.GetKey(TestContextName).Should().NotBeEmpty();
    }

    protected override void Configure(IServiceCollection services)
    {
        services.AddSingleton<IAmazonKeyManagementService>(new AmazonKeyManagementServiceClient(
                new BasicAWSCredentials("admin", "admin"),
                new AmazonKeyManagementServiceConfig { ServiceURL = localstack.Url }))
            .AddLogging(x => x.AddXUnit(helper))
            .AddAwsAesDataKeyWrapping(ConnectionString, x => x
                .WithKeyArn(localstack.TestKeyId.ToString())
                .GenerateDataKeyIfNotExist())
            .AddDbContext<TestDbContext>(x => x
                .UseNpgsql(ConnectionString)
                .UseAes256Encryption());
    }

    protected override async Task MigrateAsync(IServiceScope scope)
    {
        await scope.ServiceProvider.MigrateEncryptionContextAsync();

        await using var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await dbContext.Database.MigrateAsync();
    }
}