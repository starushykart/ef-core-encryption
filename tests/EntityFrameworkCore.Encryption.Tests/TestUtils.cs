using System.Security.Cryptography;

namespace EntityFrameworkCore.Encryption.Tests;

public static class TestUtils
{
    public static string GenerateAesKey()
    {
        using var aes = Aes.Create();
        aes.GenerateKey();
        return Convert.ToBase64String(aes.Key);
    }
}