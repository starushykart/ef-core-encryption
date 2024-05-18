using System.ComponentModel;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.TestContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.ConfigScenarios;

[Description("Testing encryption when db context configured with AddDbContext")]
public class DbContextTests(PostgresContainerFixture postgres, ITestOutputHelper helper) : BaseTest(postgres, helper, false)
{
    [Fact]
    public async Task Should_encrypt_and_decrypt_successfully()
    {
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
        var key = TestUtils.GenerateAesKeyBase64();

        services.AddDbContext<TestDbContext>(x => x
                .UseNpgsql(ConnectionString)
                .UseAes256Encryption(key));
    }
}