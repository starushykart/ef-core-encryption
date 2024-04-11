using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Common.Abstractions;

public interface IKeyProvider<TContext>
    where TContext : DbContext
{
    byte[] GetDataKey();
}