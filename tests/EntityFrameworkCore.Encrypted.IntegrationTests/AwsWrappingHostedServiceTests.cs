using Amazon.KeyManagementService;
using Amazon.Runtime;
using EntityFrameworkCore.Encrypted.Common.Abstractions;
using EntityFrameworkCore.Encrypted.IntegrationTests.Common;
using EntityFrameworkCore.Encrypted.IntegrationTests.Fixtures;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;
using EntityFrameworkCore.Encrypted.Tests.Shared;
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
    IClassFixture<LocalstackContainerFixture>,
    IClassFixture<PostgresContainerFixture>,
    IAsyncLifetime
{
    private ServiceProvider _services = null!;
    private TestInMemoryKeyStorage _keyStorage = null!;
    private IDbContextFactory<EncryptionMetadataContext> _metadataContextFactory = null!;
    private string _testContextName = null!;

    [Fact]
    public async Task Should_generate_and_save_new_data_key()
    {
        try
        {
            var wrappingService = _services.GetServices<IHostedService>().Single();

            await wrappingService.StartAsync(CancellationToken.None);

            await using var context = await _metadataContextFactory.CreateDbContextAsync();
            var metadata = await context.Metadata.SingleAsync(x => x.ContextId == _testContextName);
            metadata.Key.Should().NotBeEmpty();

            _keyStorage.ContainsKey(_testContextName).Should().BeTrue();
        }
        catch (Exception ex)
        {
            var a = _services.GetRequiredService<ILogger<AwsWrappingHostedServiceTests>>();
            a.LogError(ex, $"{ex.Message + ex.InnerException?.Message}");
            throw;
        }
    }


    public async Task InitializeAsync()
    {
        _keyStorage = new TestInMemoryKeyStorage();
        _testContextName = nameof(TestDbContext);

        _services = new ServiceCollection()
            .AddSingleton<IAmazonKeyManagementService>(new AmazonKeyManagementServiceClient(
                new BasicAWSCredentials("admin", "admin"),
                new AmazonKeyManagementServiceConfig { ServiceURL = localstack.Url }))
            .AddSingleton<IKeyStorage>(_keyStorage)
            .AddLogging(x => x.AddXUnit(helper))
            .AddAwsAesDataKeyWrapping(postgres.ConnectionString, x => x
                .WithKeyArn(localstack.TestKeyId.ToString())
                .GenerateDataKeyIfNotExist())
            .AddDbContext<TestDbContext>(x => x
                .UseNpgsql(postgres.ConnectionString)
                .UseAes256Encryption())
            .BuildServiceProvider(true);

        _metadataContextFactory = _services.GetRequiredService<IDbContextFactory<EncryptionMetadataContext>>();

        await using var scope = _services.CreateAsyncScope();
        //var dbContext = _testScope.ServiceProvider.GetRequiredService<TestDbContext>();
        //await dbContext.Database.MigrateAsync();
        await scope.ServiceProvider.MigrateEncryptionContextAsync();
    }

    public async Task DisposeAsync()
        => await _services.DisposeAsync();
}