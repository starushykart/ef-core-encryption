using System.Security.Cryptography;

namespace EntityFrameworkCore.Encrypted.Tests.Shared;

public static class TestUtils
{
    public static string GenerateAesKeyBase64()
    {
        using var aes = Aes.Create();
        aes.GenerateKey();
        return Convert.ToBase64String(aes.Key);
    }
}