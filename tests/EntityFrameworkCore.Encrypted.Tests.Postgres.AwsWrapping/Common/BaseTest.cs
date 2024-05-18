using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Database;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Shared;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common;

[Collection(nameof(IntegrationTestsCollection))]
public abstract class BaseTest(PostgresContainerFixture postgres, ITestOutputHelper helper, bool useFactory) : BaseSharedTest(postgres, helper)
{
    protected override async Task MigrateAsync(IServiceProvider provider)
    {
        await provider.MigrateEncryptionContextAsync();
        await provider.MigrateContextAsync<TestDbContext>(useFactory);
    }
}