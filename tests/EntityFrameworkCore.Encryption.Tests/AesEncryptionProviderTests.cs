using System.Text;
using Bogus;
using EntityFrameworkCore.Encryption.Internal.Providers;
using EntityFrameworkCore.Encryption.Internal.Providers.AesProvider;
using FluentAssertions;
using Xunit;

namespace EntityFrameworkCore.Encryption.Tests;

public class AesEncryptionProviderTests
{
    private readonly Faker _faker = new();

    [Fact]
    public void Should_encrypt_and_decrypt_successfully()
    {   
        var keyProvider = new ConstantKeyProvider<TestDbContext>(TestUtils.GenerateAesKey());
        var provider = new AesEncryptionProvider<TestDbContext>(keyProvider);

        var stringToEncrypt = _faker.Lorem.Sentence();
        var bytesToEncrypt = Encoding.UTF8.GetBytes(stringToEncrypt);

        var encryptedBytes = provider.Encrypt(bytesToEncrypt);
        var decryptedBytes = provider.Decrypt(encryptedBytes);
        
        var decryptedString = Encoding.UTF8.GetString(decryptedBytes!);

        decryptedBytes.Should().BeEquivalentTo(bytesToEncrypt);
        stringToEncrypt.Should().Be(decryptedString);
    }
}