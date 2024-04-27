namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Common.Abstractions;

public interface IMemoryKeyStorage
{
    internal byte[] GetKey(string contextName);
}