using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Encryption.Tests;

public class TestDbContext(DbContextOptions options) : EncryptedDbContext(options)
{
    
}