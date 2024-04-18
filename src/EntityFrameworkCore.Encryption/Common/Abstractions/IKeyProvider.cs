namespace EntityFrameworkCore.Encryption.Common.Abstractions;

public interface IKeyProvider
{
    byte[] GetDataKey();
}

