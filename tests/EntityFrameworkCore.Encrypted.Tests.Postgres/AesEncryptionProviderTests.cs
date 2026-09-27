using System.Security.Cryptography;
using System.Text;
using Bogus;
using EntityFrameworkCore.Encrypted.Providers;
using FluentAssertions;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

public class AesEncryptionProviderTests
{
    private readonly Faker _faker = new();

    [Fact]
    public void Should_encrypt_and_decrypt_successfully()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var provider = new Aes256EncryptionProvider(() => key);

        var stringToEncrypt = _faker.Lorem.Sentence();
        var bytesToEncrypt = Encoding.UTF8.GetBytes(stringToEncrypt);

        var encryptedBytes = provider.Encrypt(bytesToEncrypt);
        var decryptedBytes = provider.Decrypt(encryptedBytes);

        var decryptedString = Encoding.UTF8.GetString(decryptedBytes!);

        decryptedBytes.Should().BeEquivalentTo(bytesToEncrypt);
        stringToEncrypt.Should().Be(decryptedString);
    }
}
