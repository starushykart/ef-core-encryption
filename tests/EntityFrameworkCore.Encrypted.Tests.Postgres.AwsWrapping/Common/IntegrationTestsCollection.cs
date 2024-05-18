using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.Fixtures;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common;

[CollectionDefinition(nameof(IntegrationTestsCollection))]
public class IntegrationTestsCollection : 
    ICollectionFixture<PostgresContainerFixture>,
    ICollectionFixture<LocalstackContainerFixture>;