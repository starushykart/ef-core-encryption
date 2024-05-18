using System.ComponentModel;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping;
using EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.TestContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.ConfigScenarios;

[Description("Testing encryption when db context configured with AddDbContextFactory")]
public class DbContextFactoryTests(
    PostgresContainerFixture postgres,
    LocalstackContainerFixture localstack,
    ITestOutputHelper helper) : BaseTest(postgres, helper, true)
{
    [Fact]
    public async Task Should_encrypt_and_decrypt_successfully()
    {
        await Provider.RunAwsWrappingHostedServiceAsync();
        
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
            .AddAwsAesDataKeyWrapping(ConnectionString, x => x
                .WithKeyArn(localstack.TestKeyId.ToString())
                .GenerateDataKeyIfNotExist())
            .AddDbContextFactory<TestDbContext>(x => x
                .UseNpgsql(ConnectionString)
                .UseAes256Encryption());
    }
}