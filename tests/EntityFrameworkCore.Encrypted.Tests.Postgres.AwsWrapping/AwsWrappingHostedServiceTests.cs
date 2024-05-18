using Amazon.KeyManagementService;
using EntityFrameworkCore.Encrypted.Common.Abstractions;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common.Extensions;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Services;
using EntityFrameworkCore.Encrypted.Providers;
using EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping;

public class AwsWrappingHostedServiceTests(
    LocalstackContainerFixture localstack,
    PostgresContainerFixture postgres,
    ITestOutputHelper helper) :
    BaseTest(postgres, helper, false)
{
    private const string TestContextName = nameof(TestDbContext);
    
    [Fact]
    public async Task Should_generate_and_save_new_data_key()
    {
        await Provider.RunAwsWrappingHostedServiceAsync();

        var metadataContextFactory = Provider.GetRequiredService<IDbContextFactory<EncryptionMetadataContext>>();
        await using var context = await metadataContextFactory.CreateDbContextAsync();
        
        var metadata = await context.Metadata
            .SingleAsync(x => x.ContextId == TestContextName);

        metadata.ContextId.Should().Be(TestContextName);
        metadata.Key.Should().NotBeEmpty();

        InMemoryKeyStorage.Instance.ContainsKey(TestContextName).Should().BeTrue();
        InMemoryKeyStorage.Instance.GetKey(TestContextName).Should().NotBeEmpty();
    }
    
    [Fact]
    public async Task Should_re_encrypt_data_key()
    {
        await Provider.RunAwsWrappingHostedServiceAsync();
        InMemoryKeyStorage.Instance.Clear();
        await Provider.RunAwsWrappingHostedServiceAsync();

        var metadataContextFactory = Provider.GetRequiredService<IDbContextFactory<EncryptionMetadataContext>>();
        await using var context = await metadataContextFactory.CreateDbContextAsync();
        
        var metadata = await context.Metadata
            .SingleAsync(x => x.ContextId == TestContextName);

        metadata.ContextId.Should().Be(TestContextName);
        metadata.Key.Should().NotBeEmpty();

        InMemoryKeyStorage.Instance.ContainsKey(TestContextName).Should().BeTrue();
        InMemoryKeyStorage.Instance.GetKey(TestContextName).Should().NotBeEmpty();
    }
    
    [Fact]
    public async Task Should_throw_if_key_not_exist_and_GenerateDataKeyIfNotExist_false()
    {
        var wrappingOptions = new AwsWrappingOptionsBuilder()
            .WithKeyArn(localstack.TestKeyId.ToString())
            .GenerateDataKeyIfNotExist(false)
            .Build();

        var service = new AwsKeyWrappingHostedService(
            Provider.GetRequiredService<IServiceScopeFactory>(),
            Provider.GetRequiredService<IKeyStorage>(),
            Provider.GetRequiredService<IAmazonKeyManagementService>(),
            Provider.GetRequiredService<IDbContextFactory<EncryptionMetadataContext>>(),
            wrappingOptions,
            new NullLogger<AwsKeyWrappingHostedService>());

        var act = async () => await service.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<EntityFrameworkEncryptionException>();
    }
    
    protected override void Configure(IServiceCollection services)
    {
        services
            .AddLocalstackKms(localstack)
            .AddDbContext<TestDbContext>(x => x
                    .UseNpgsql(ConnectionString)
                    .UseAes256Encryption(),
                x => x
                    .WithKeyArn(localstack.TestKeyId.ToString())
                    .GenerateDataKeyIfNotExist());
    }
}