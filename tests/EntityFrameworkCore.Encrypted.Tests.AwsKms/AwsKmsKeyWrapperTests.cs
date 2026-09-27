using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using EntityFrameworkCore.Encrypted.Common.Storage;
using EntityFrameworkCore.Encrypted.Tests.AwsKms.Common;
using EntityFrameworkCore.Encrypted.Tests.AwsKms.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.AwsKms.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using AwsKmsOptions = EntityFrameworkCore.Encrypted.AwsKms.AwsKmsOptions;
using BaseTest = EntityFrameworkCore.Encrypted.Tests.AwsKms.Common.BaseTest;

namespace EntityFrameworkCore.Encrypted.Tests.AwsKms;

public class AwsKmsKeyWrapperTests(
    LocalstackContainerFixture localstack,
    PostgresContainerFixture postgres,
    ITestOutputHelper helper) :
    BaseTest(postgres, helper, false)
{
    private const string GenerateDataKey = nameof(IAmazonKeyManagementService.GenerateDataKeyAsync);
    private const string Decrypt = nameof(IAmazonKeyManagementService.DecryptAsync);
    private const string ReEncrypt = nameof(IAmazonKeyManagementService.ReEncryptAsync);

    [Fact]
    public async Task Should_generate_root_key_with_kms_on_first_start()
    {
        var (provider, kms) = BuildProvider();
        await using var _ = provider;

        await provider.InitializeEncryptionAsync();

        var stored = await GetStoredKeysAsync();
        stored.Should().ContainSingle();
        stored[0].WrappingKeyId.Should().StartWith("arn:aws:kms:", "the ARN of the wrapping key is stored");
        kms[Op(GenerateDataKey)].Should().Be(1);
        kms[Op(Decrypt)].Should().Be(0);
    }

    [Fact]
    public async Task Should_make_single_kms_call_per_start()
    {
        var original = Fakers.PasswordFaker.Generate();
        var (first, _) = BuildProvider();

        await using (first)
        await using (var scope = first.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            context.Add(original);
            await context.SaveChangesAsync();
        }

        var (restarted, kms) = BuildProvider();
        await using var __ = restarted;
        await restarted.InitializeEncryptionAsync();

        await using var restartedScope = restarted.CreateAsyncScope();
        var restartedContext = restartedScope.ServiceProvider.GetRequiredService<TestDbContext>();
        foreach (var _ in Enumerable.Range(0, 10))
        {
            restartedContext.ChangeTracker.Clear();
            original.AssertPasswordEncryption(await restartedContext.Passwords.SingleAsync(x => x.Id == original.Id));
        }

        kms.Calls.Should().BeEquivalentTo(new Dictionary<string, int> { [Op(Decrypt)] = 1 });
    }

    [Fact]
    public async Task Should_rotate_root_key_and_keep_older_values_readable()
    {
        var original = Fakers.PasswordFaker.Generate();
        var (provider, kms) = BuildProvider();
        await using var _ = provider;

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            context.Add(original);
            await context.SaveChangesAsync();
        }

        var rootKeyId = await provider.RotateRootKeyAsync<TestDbContext>();

        rootKeyId.Should().Be(2);
        (await GetStoredKeysAsync()).Select(x => x.Id).Should().Equal(1, 2);
        kms[Op(GenerateDataKey)].Should().Be(2);

        var (restarted, restartedKms) = BuildProvider();
        await using var __ = restarted;
        await using var restartedScope = restarted.CreateAsyncScope();

        original.AssertPasswordEncryption(await restartedScope.ServiceProvider.GetRequiredService<TestDbContext>()
            .Passwords.SingleAsync(x => x.Id == original.Id));
        restartedKms[Op(Decrypt)].Should().Be(2, "active root key on start, the older one when its value is read");
    }

    [Fact]
    public async Task Should_bind_wrapped_root_key_to_its_id()
    {
        var (provider, _) = BuildProvider();
        await using (provider)
            await provider.InitializeEncryptionAsync();

        // pretend the wrapped key of root key 1 is root key 2
        await using (var scope = Provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database
                .ExecuteSqlRawAsync($"""UPDATE "{EncryptionKeyEntity.TableName}" SET "Id" = 2""");
        }

        var (restarted, _) = BuildProvider();
        await using var __ = restarted;

        var act = () => restarted.InitializeEncryptionAsync();

        await act.Should().ThrowAsync<AmazonKeyManagementServiceException>();
    }

    [Fact]
    public async Task Should_require_same_encryption_context_to_unwrap()
    {
        var (orders, _) = BuildProvider(x => x.WithEncryptionContext("service", "orders"));
        await using (orders)
            await orders.InitializeEncryptionAsync();

        var (billing, _) = BuildProvider(x => x.WithEncryptionContext("service", "billing"));
        await using var _ = billing;

        var act = () => billing.InitializeEncryptionAsync();

        await act.Should().ThrowAsync<AmazonKeyManagementServiceException>();
    }

    [Fact]
    public async Task Should_move_root_keys_to_another_kms_key()
    {
        var kmsClient = Common.Extensions.TestsExtensions.CreateLocalstackKmsClient(localstack);
        var oldKey = (await kmsClient.CreateKeyAsync(new CreateKeyRequest())).KeyMetadata;
        var newKey = (await kmsClient.CreateKeyAsync(new CreateKeyRequest())).KeyMetadata;
        var original = Fakers.PasswordFaker.Generate();

        var (before, _) = BuildProvider(x => x.WithKeyId(oldKey.KeyId));
        await using (before)
        {
            await using (var scope = before.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                context.Add(original);
                await context.SaveChangesAsync();
            }

            await before.RotateRootKeyAsync<TestDbContext>();
        }

        var (moved, kms) = BuildProvider(x => x.WithKeyId(newKey.KeyId));
        await using (moved)
            (await moved.RewrapRootKeysAsync<TestDbContext>()).Should().Be(2);

        kms[Op(ReEncrypt)].Should().Be(2);
        (await GetStoredKeysAsync()).Should().OnlyContain(x => x.WrappingKeyId == newKey.Arn);

        await kmsClient.DisableKeyAsync(new DisableKeyRequest { KeyId = oldKey.KeyId });

        var (restarted, _) = BuildProvider(x => x.WithKeyId(newKey.KeyId));
        await using var _ = restarted;
        await using var restartedScope = restarted.CreateAsyncScope();
        original.AssertPasswordEncryption(await restartedScope.ServiceProvider.GetRequiredService<TestDbContext>()
            .Passwords.SingleAsync(x => x.Id == original.Id));
    }

    protected override void Configure(IServiceCollection services)
        => services
            .AddLocalstackKms(localstack)
            .AddEncryption(x => x.UseAwsKms(localstack.TestKeyId.ToString()))
            .AddDbContext<TestDbContext>(x => x.UseNpgsql(ConnectionString).UseEncryption());

    private (ServiceProvider Provider, CountingKmsClient Kms) BuildProvider(Action<AwsKmsOptions>? configure = null)
    {
        var (client, counter) = CountingKmsClient.Wrap(Common.Extensions.TestsExtensions.CreateLocalstackKmsClient(localstack));

        var provider = new ServiceCollection()
            .AddSingleton(client)
            .AddEncryption(x => x.UseAwsKms(kms =>
            {
                kms.WithKeyId(localstack.TestKeyId.ToString());
                configure?.Invoke(kms);
            }))
            .AddDbContext<TestDbContext>(x => x.UseNpgsql(ConnectionString).UseEncryption())
            .AddXunitLogging(Helper)
            .BuildServiceProvider(true);

        return (provider, counter);
    }

    private async Task<List<EncryptionKeyEntity>> GetStoredKeysAsync()
    {
        await using var scope = Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TestDbContext>()
            .Set<EncryptionKeyEntity>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
    }

    private static string Op(string methodName)
        => methodName.Replace("Async", string.Empty);
}
