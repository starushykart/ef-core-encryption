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

[Description("Testing encryption when db context configured with AddDbContext")]
public class DbContextTests(
    PostgresContainerFixture postgres,
    LocalstackContainerFixture localstack,
    ITestOutputHelper helper) : BaseTest(postgres, helper, false)
{
    [Fact]
    public async Task Should_encrypt_and_decrypt_successfully()
    {
        await Provider.RunAwsWrappingHostedServiceAsync();
        
        await using var scope = Provider.CreateAsyncScope();
        await using var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        
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
            .AddDbContext<TestDbContext>(x => x
                    .UseNpgsql(ConnectionString)
                    .UseAes256Encryption(),
                x => x
                    .WithKeyArn(localstack.TestKeyId.ToString())
                    .GenerateDataKeyIfNotExist());
    }
}