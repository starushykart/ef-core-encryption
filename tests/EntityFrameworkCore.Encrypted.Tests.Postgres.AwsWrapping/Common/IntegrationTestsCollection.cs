using EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Fixtures;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common;

[CollectionDefinition(nameof(IntegrationTestsCollection))]
public class IntegrationTestsCollection : 
    ICollectionFixture<PostgresContainerFixture>,
    ICollectionFixture<LocalstackContainerFixture>;