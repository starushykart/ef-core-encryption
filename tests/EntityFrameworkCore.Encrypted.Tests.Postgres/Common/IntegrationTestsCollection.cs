using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.Fixtures;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common;

[CollectionDefinition(nameof(IntegrationTestsCollection))]
public class IntegrationTestsCollection : 
    ICollectionFixture<PostgresContainerFixture>;