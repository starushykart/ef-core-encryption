using System.Security.Cryptography;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Shared;

public static class TestUtils
{
    public static string GenerateAesKeyBase64()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}