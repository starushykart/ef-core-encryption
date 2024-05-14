namespace EntityFrameworkCore.Encrypted.Common.Abstractions;

public interface IKeyProvider
{
    byte[] GetKey();
}

