using Bogus;

namespace EntityFrameworkCore.Encrypted.IntegrationTests.Common;

public static class Fakers
{
    public static Faker<Password> PasswordFaker => new Faker<Password>()
        .CustomInstantiator(f =>
        {
            var passwordValue = f.Random.Word();
            return new Password
            {
                Id = f.Random.Guid(),
                EncryptedFluent = passwordValue,
                EncryptedAttribute = passwordValue,
                Original = passwordValue
            };
        });
}