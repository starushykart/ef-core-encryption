using Bogus;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common;

public static class Fakers
{
    public static Faker<Password> PasswordFaker => new Faker<Password>()
        .CustomInstantiator(f =>
        {
            var passwordValue = f.Random.AlphaNumeric(10);
            return new Password
            {
                Id = f.Random.Guid(),
                EncryptedFluent = passwordValue,
                EncryptedAttribute = passwordValue,
                Original = passwordValue
            };
        });
}