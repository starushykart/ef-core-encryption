using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using FluentAssertions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;

public static class AssertionExtensions
{
    public static void AssertPasswordEncryption(this Password original, Password? persisted)
    {
        persisted.Should().NotBeNull();
        persisted.Should().BeEquivalentTo(original);

        persisted!
            .Original.Should().Be(persisted.EncryptedAttribute)
            .And.Be(persisted.EncryptedFluent);
    }
}