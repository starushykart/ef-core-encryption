namespace EntityFrameworkCore.Encrypted.Common.Abstractions;

public interface IKeyStorage
{
    void AddKey(string contextName, byte[] key);
    byte[] GetKey(string contextName);
    bool ContainsKey(string contextName);
}