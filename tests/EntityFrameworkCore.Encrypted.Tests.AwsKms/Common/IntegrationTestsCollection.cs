using EntityFrameworkCore.Encrypted.Tests.AwsKms.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;

namespace EntityFrameworkCore.Encrypted.Tests.AwsKms.Common;

[CollectionDefinition(nameof(IntegrationTestsCollection))]
public class IntegrationTestsCollection : 
    ICollectionFixture<PostgresContainerFixture>,
    ICollectionFixture<LocalstackContainerFixture>;