using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.Extensions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common;

[Collection(nameof(IntegrationTestsCollection))]
public abstract class BaseTest(PostgresContainerFixture postgres, ITestOutputHelper helper, bool useFactory) : BaseSharedTest(postgres, helper)
{
    protected override Task MigrateAsync(IServiceProvider provider)
        => provider.MigrateTestContextAsync(useFactory);
}