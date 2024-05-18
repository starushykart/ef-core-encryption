using EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common.Extensions;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;
using EntityFrameworkCore.Encrypted.Providers;
using EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.TestContext;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping;

public class AwsWrappingHostedServiceTests(
    LocalstackContainerFixture localstack,
    PostgresContainerFixture postgres,
    ITestOutputHelper helper) :
    BaseTest(postgres, helper)
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