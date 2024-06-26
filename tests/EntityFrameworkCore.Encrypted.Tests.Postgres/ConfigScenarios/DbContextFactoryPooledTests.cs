using System.ComponentModel;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Shared;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.ConfigScenarios;

[Description("Testing encryption when db context configured with AddPooledDbContextFactory")]
public class DbContextFactoryPooledTests(PostgresContainerFixture postgres, ITestOutputHelper helper) : BaseTest(postgres, helper, true)
{
    [Fact]
    public async Task Should_encrypt_and_decrypt_successfully()
    {
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
        var key = TestUtils.GenerateAesKeyBase64();

        services.AddPooledDbContextFactory<TestDbContext>(x => x
            .UseNpgsql(ConnectionString)
            .UseAes256Encryption(key), 2);
    }
}