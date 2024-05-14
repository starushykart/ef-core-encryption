namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common.Abstractions;

public interface IMemoryKeyStorage
{
    internal byte[] GetKey(string contextName);
}