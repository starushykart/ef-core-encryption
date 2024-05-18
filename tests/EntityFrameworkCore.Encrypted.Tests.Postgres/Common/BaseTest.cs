using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Shared;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common;

[Collection(nameof(IntegrationTestsCollection))]
public abstract class BaseTest(PostgresContainerFixture postgres, ITestOutputHelper helper, bool useFactory) : BaseSharedTest(postgres, helper)
{
    protected override Task MigrateAsync(IServiceProvider provider)
        => provider.MigrateContextAsync<TestDbContext>(useFactory);
}