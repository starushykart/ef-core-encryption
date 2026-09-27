# Entity Framework Core Encrypted

[![Build and Test](https://github.com/starushykart/ef-core-encryption/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/build-and-test.yml)
[![CodeQL](https://github.com/starushykart/ef-core-encryption/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/github-code-scanning/codeql)
[![Qodana](https://github.com/starushykart/ef-core-encryption/actions/workflows/code_quality.yml/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/code_quality.yml)
[![codecov](https://codecov.io/github/starushykart/ef-core-encryption/graph/badge.svg?token=C1JOFN38GC)](https://codecov.io/github/starushykart/ef-core-encryption)

## Disclaimer
This project is an extension of [Microsoft Entity Framework Core](https://github.com/aspnet/EntityFrameworkCore) that adds support for encrypted properties using built-in or custom encryption providers.  
  
The authors **do not accept any responsibility** if you use or deploy this in a production environment and lose your encryption key or corrupt your data. Users are advised to thoroughly test and validate integration before using it in any production environment.
  
By using or contributing to this repository, you agree to follow its terms.

## NuGet
|                                                    |      |                                                                                                                                                                                         |                                                                                                                                                                                             |
|----------------------------------------------------|:----:|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------:|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------:|
| EntityFrameworkCore.Encrypted                      |.NET 10| [![NuGet Version](https://img.shields.io/nuget/v/EntityFrameworkCore.Encrypted)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted)                                          | [![NuGet Downloads](https://img.shields.io/nuget/dt/EntityFrameworkCore.Encrypted)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted)                                           |
| EntityFrameworkCore.Encrypted.AwsKms                |.NET 10| [![NuGet Version](https://img.shields.io/nuget/v/EntityFrameworkCore.Encrypted.AwsKms)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted.AwsKms)| [![NuGet Downloads](https://img.shields.io/nuget/dt/EntityFrameworkCore.Encrypted.AwsKms)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted.AwsKms) |

## GitHub Issues
![GitHub Issues or Pull Requests](https://img.shields.io/github/issues-raw/starushykart/ef-core-encryption?link=https%3A%2F%2Fgithub.com%2Fstarushykart%2Fef-core-encryption%2Fissues%3Fq%3Dis%253Aopen%2Bis%253Aissue%2B)

## Build from Source

 1. Install the latest [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
 2. Clone the source codee<br/>
    ```bash
    git clone https://github.com/starushykart/ef-core-encryption.git
    ```
 3. Run `dotnet build`

## Usage

```csharp
builder.Services
    .AddEncryption(x => x.UseAwsKms("arn:aws:kms:eu-west-1:123456789012:key/..."))   // or x.UseKey(key)
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

`string` and `byte[]` properties are supported. Values are encrypted with AES-256-GCM and bound to their column (`"{table}.{column}"`); pass a label (`IsEncrypted("users.email")`, `[Encrypted(Label = "users.email")]`) to keep values readable after renaming the column. Strings are stored as Base64 text, binary values as binary.

Every encrypted context gets an `__EncryptionKeys` table: add a migration after enabling encryption.

Encrypted values use a random nonce, so the database can't compare, search, sort or group them. Such queries (`Where(x => x.Email == email)`, `Contains`, `OrderBy(x => x.Email)`, ...) throw instead of returning wrong results; only `== null` and `!= null` are supported.

### Keys

```
KMS key ──wraps──► root key (__EncryptionKeys table)      1 KMS Decrypt per context on startup
root key ──HKDF──► data key v{n}                           derived in memory, never stored
```

- **Root key**: generated by KMS on first start and stored wrapped in the context's own database, so backups and replicas contain the keys they need. Only `kms:Decrypt` is needed afterwards; disable creation with `CreateRootKeyIfMissing(false)`.
- **Data key**: derived from the root key in memory. Rotate it with `UseDataKeyVersion(n)` (default 0): no KMS calls, values encrypted with previous versions stay readable.
- **Root key rotation**: `await app.Services.RotateRootKeyAsync<AppDbContext>()`. New values use the new root key; older values stay readable. Other instances switch to it when they read a value encrypted with it, or on restart.
- **KMS key rotation**: enable automatic rotation in KMS; nothing to do in the application.

Keys are loaded when the host starts, before hosted services run. Without a generic host call `await app.Services.InitializeEncryptionAsync()`, otherwise keys are loaded on first use.


