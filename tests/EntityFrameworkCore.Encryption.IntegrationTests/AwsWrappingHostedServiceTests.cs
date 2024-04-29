using Amazon.KeyManagementService;
using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.IntegrationTests.Common;
using EntityFrameworkCore.Encryption.IntegrationTests.Fixtures;
using EntityFrameworkCore.Encryption.Postgres.AwsWrapping;
using EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Database;
using EntityFrameworkCore.Encryption.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EntityFrameworkCore.Encryption.IntegrationTests;

public class AwsWrappingHostedServiceTests(LocalstackContainerFixture localstack, PostgresContainerFixture postgres) :
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
            Console.WriteLine(ex.Message + ex.InnerException?.Message);
            throw;
        }
    }


    public async Task InitializeAsync()
    {
        _keyStorage = new TestInMemoryKeyStorage();
        _testContextName = nameof(TestDbContext);
        
        _services = new ServiceCollection()
            .AddSingleton<IAmazonKeyManagementService>(new AmazonKeyManagementServiceClient(
                new AmazonKeyManagementServiceConfig { ServiceURL = localstack.Url }))
            .AddSingleton<IKeyStorage>(_keyStorage)
            .AddLogging()
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