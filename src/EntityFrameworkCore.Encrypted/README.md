# EntityFrameworkCore.Encrypted

Application-side encryption of EF Core entity properties with AES-256-GCM. Values are encrypted before they reach the database and decrypted when they are read.

## Usage

```csharp
builder.Services
    .AddEncryption(x => x.UseKey(builder.Configuration["Encryption:Key"]!))   // base64, 32 bytes
    .AddDbContext<AppDbContext>(x => x.UseNpgsql(connectionString).UseEncryption());
```

```csharp
public class User
{
    public int Id { get; set; }

    [Encrypted]
    public string Email { get; set; } = null!;

    [Encrypted]
    public byte[]? Document { get; set; }
}

// or in OnModelCreating
modelBuilder.Entity<User>().Property(x => x.Email).IsEncrypted();
```

Add a migration after enabling encryption: every encrypted context gets an `__EncryptionKeys` table.

## Features

- `string` and `byte[]` properties; `null` stays `null`
- Tamper detection, values bound to their column
- Key rotation without re-encrypting data; `ReEncryptAsync` and `GetKeyUsageAsync` to retire old keys
- Envelope encryption with a key management service: [EntityFrameworkCore.Encrypted.AwsKms](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted.AwsKms)
- Queries that compare, search or sort encrypted columns throw instead of returning wrong results

Documentation: https://github.com/starushykart/ef-core-encryption
