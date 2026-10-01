using System.ComponentModel;
using EntityFrameworkCore.Encrypted.Tests.AwsKms.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.AwsKms.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BaseTest = EntityFrameworkCore.Encrypted.Tests.AwsKms.Common.BaseTest;

namespace EntityFrameworkCore.Encrypted.Tests.AwsKms.ConfigScenarios;

[Description("Testing encryption when db context configured with AddDbContextFactory")]
public class DbContextFactoryTests(
    PostgresContainerFixture postgres,
    LocalstackContainerFixture localstack,
    ITestOutputHelper helper) : BaseTest(postgres, helper, true)
{
    [Fact]
    public async Task Should_encrypt_and_decrypt_successfully()
    {
        await Provider.InitializeEncryptionAsync();

        var factory = Provider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        await using var context = await factory.CreateDbContextAsync();

        var original = Fakers.PasswordFaker.Generate();

        context.Add(original);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var persisted = await context.Passwords
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == original.Id);

        original.AssertPasswordEncryption(persisted);
    }

    protected override void Configure(IServiceCollection services)
    {
        services
            .AddLocalstackKms(localstack)
            .AddEncryption(x => x.UseAwsKms(localstack.TestKeyId.ToString()))
            .AddDbContextFactory<TestDbContext>(x => x.UseNpgsql(ConnectionString).UseEncryption());
    }
}
