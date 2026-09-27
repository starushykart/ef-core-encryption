using Amazon.KeyManagementService;
using EntityFrameworkCore.Encrypted.Common.Abstractions;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common.Extensions;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Services;
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

public class AwsKmsDataKeySourceTests(
    LocalstackContainerFixture localstack,
    PostgresContainerFixture postgres,
    ITestOutputHelper helper) :
    BaseTest(postgres, helper, false)
{
    private const string TestContextName = nameof(TestDbContext);
    private static readonly DataKeyContext TestDataKeyContext = new(typeof(TestDbContext));

    [Fact]
    public async Task Should_generate_and_save_new_data_key()
    {
        await Provider.InitializeEncryptionAsync();

        var metadata = await GetMetadataAsync();

        metadata.ContextId.Should().Be(TestContextName);
        metadata.Key.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Should_re_encrypt_data_key()
    {
        var source = Provider.GetRequiredService<AwsKmsDataKeySource>();

        var generatedKey = await source.GetDataKeyAsync(TestDataKeyContext, CancellationToken.None);
        var generatedMetadata = await GetMetadataAsync();

        var reEncryptedKey = await source.GetDataKeyAsync(TestDataKeyContext, CancellationToken.None);
        var reEncryptedMetadata = await GetMetadataAsync();

        reEncryptedKey.Should().Equal(generatedKey);
        reEncryptedMetadata.Key.Should().NotEqual(generatedMetadata.Key);
    }

    [Fact]
    public async Task Should_throw_if_key_not_exist_and_GenerateDataKeyIfNotExist_false()
    {
        var wrappingOptions = new AwsWrappingOptionsBuilder()
            .WithKeyArn(localstack.TestKeyId.ToString())
            .GenerateDataKeyIfNotExist(false)
            .Build();

        var source = new AwsKmsDataKeySource(
            Provider.GetRequiredService<IAmazonKeyManagementService>(),
            Provider.GetRequiredService<IDbContextFactory<EncryptionMetadataContext>>(),
            wrappingOptions,
            new NullLogger<AwsKmsDataKeySource>());

        var act = async () => await source.GetDataKeyAsync(TestDataKeyContext, CancellationToken.None);

        await act.Should().ThrowAsync<EntityFrameworkEncryptionException>();
    }

    protected override void Configure(IServiceCollection services)
    {
        services
            .AddLocalstackKms(localstack)
            .AddDbContext<TestDbContext>(x => x
                    .UseNpgsql(ConnectionString)
                    .UseEncryption(),
                x => x
                    .WithKeyArn(localstack.TestKeyId.ToString())
                    .GenerateDataKeyIfNotExist());
    }

    private async Task<EncryptionMetadata> GetMetadataAsync()
    {
        var metadataContextFactory = Provider.GetRequiredService<IDbContextFactory<EncryptionMetadataContext>>();
        await using var context = await metadataContextFactory.CreateDbContextAsync();

        return await context.Metadata
            .AsNoTracking()
            .SingleAsync(x => x.ContextId == TestContextName);
    }
}
