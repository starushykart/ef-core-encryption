# Entity Framework Core Encrypted

[![Build and Test](https://github.com/starushykart/ef-core-encryption/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/build-and-test.yml)
[![CodeQL](https://github.com/starushykart/ef-core-encryption/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/github-code-scanning/codeql)
[![Qodana](https://github.com/starushykart/ef-core-encryption/actions/workflows/code_quality.yml/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/code_quality.yml)
[![codecov](https://codecov.io/github/starushykart/ef-core-encryption/graph/badge.svg?token=C1JOFN38GC)](https://codecov.io/github/starushykart/ef-core-encryption)

## Disclaimer
This project is an extension of [Microsoft Entity Framework Core](https://github.com/aspnet/EntityFrameworkCore) that encrypts entity properties with AES-256-GCM, with keys managed in code or by a key management service such as AWS KMS. It works with any EF Core relational provider (tested with PostgreSQL, SQL Server and SQLite).  
  
The authors **do not accept any responsibility** if you use or deploy this in a production environment and lose your encryption key or corrupt your data. Users are advised to thoroughly test and validate integration before using it in any production environment.
  
By using or contributing to this repository, you agree to follow its terms.

## NuGet
|                                                    |      |                                                                                                                                                                                         |                                                                                                                                                                                             |
|----------------------------------------------------|:----:|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------:|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------:|
| EntityFrameworkCore.Encrypted                      |.NET 10| [![NuGet Version](https://img.shields.io/nuget/vpre/EntityFrameworkCore.Encrypted)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted)                                          | [![NuGet Downloads](https://img.shields.io/nuget/dt/EntityFrameworkCore.Encrypted)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted)                                           |
| EntityFrameworkCore.Encrypted.AwsKms                |.NET 10| [![NuGet Version](https://img.shields.io/nuget/vpre/EntityFrameworkCore.Encrypted.AwsKms)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted.AwsKms)| [![NuGet Downloads](https://img.shields.io/nuget/dt/EntityFrameworkCore.Encrypted.AwsKms)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted.AwsKms) |

## GitHub Issues
![GitHub Issues or Pull Requests](https://img.shields.io/github/issues-raw/starushykart/ef-core-encryption?link=https%3A%2F%2Fgithub.com%2Fstarushykart%2Fef-core-encryption%2Fissues%3Fq%3Dis%253Aopen%2Bis%253Aissue%2B)

## Build from Source

 1. Install the latest [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
 2. Clone the source code<br/>
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

`string` and `byte[]` properties are supported, including properties of complex types. Other types are encrypted after a configured conversion to `string` or `byte[]`:

```csharp
modelBuilder.Entity<User>().Property(x => x.Status).HasConversion<string>().IsEncrypted();     // enum as text
modelBuilder.Entity<User>().Property(x => x.Birthday).HasConversion<string>().IsEncrypted();   // DateOnly
```

Values are encrypted with AES-256-GCM and bound to their column (`"{table}.{column}"`); pass a label (`IsEncrypted("users.email")`, `[Encrypted(Label = "users.email")]`) to keep values readable after renaming the column. Strings are stored as Base64 text, binary values as binary. Encrypted columns can't have a maximum length (`MaxLength`, or a size in `HasColumnType`): it would limit the ciphertext, not the value.

Every encrypted context gets an `__EncryptionKeys` table: add a migration after enabling encryption.

Encrypted values use a random nonce, so the database can't compare, search, sort or group them. Such queries (`Where(x => x.Email == email)`, `Contains`, `OrderBy(x => x.Email)`, ...) throw instead of returning wrong results; only `== null` and `!= null` are supported.

### Keys

```
KMS key ──wraps──► root key (__EncryptionKeys table)      1 KMS Decrypt per context on startup
root key ──HKDF──► data key v{n}                           derived in memory, never stored
```

- **Root key**: generated by KMS on first start and stored wrapped in the context's own database, so backups and replicas contain the keys they need. Only `kms:Decrypt` is needed afterwards; disable creation with `CreateRootKeyIfMissing(false)`.
- **Data key**: derived from the root key in memory. Rotate it with `UseDataKeyVersion(n)` (default 0): no KMS calls, values encrypted with previous versions stay readable.
- **Root key rotation**: `await app.Services.RotateRootKeyAsync<AppDbContext>()`. New values use the new root key; older values stay readable. Other instances switch to it when they read a value encrypted with it, or on restart. Instances that only write can check for it periodically with `RefreshRootKeysEvery(TimeSpan.FromMinutes(5))` (one key table read per check).
- **KMS key rotation**: enable automatic rotation in KMS; nothing to do in the application.
- **Moving to another KMS key**: configure the new key, then `await app.Services.RewrapRootKeysAsync<AppDbContext>()`. Root keys are re-encrypted inside KMS (`ReEncrypt`, needs `kms:ReEncryptFrom` on the old key and `kms:ReEncryptTo` on the new one); values are not touched. Disable the old key afterwards.

### Retiring keys

```csharp
var usage = await app.Services.GetKeyUsageAsync<AppDbContext>();     // values per table, column and key
var result = await app.Services.ReEncryptAsync<AppDbContext>();      // re-encrypt values not using the active key
```

`ReEncryptAsync` re-encrypts values with the active root key and data key version in batches (`batchSize`, default 1000), reading and updating encrypted columns directly, without loading entities. It is safe to run while the application is running and to run again: values changed by the application in the meantime are left as is (`Skipped`), values that can't be decrypted are reported (`Invalid`). When `GetKeyUsageAsync` no longer lists a root key, no value in the database uses it. Keep it in `__EncryptionKeys` anyway if backups taken before re-encrypting may be restored.

### Health check

```csharp
builder.Services.AddHealthChecks().AddEncryptionKeys();
```

Healthy when the keys of every encrypted context are loaded; keys that aren't loaded yet are loaded by the check (key store migrated and reachable, key management service available). Once loaded, the check makes no calls.

### OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(x => x.AddSource(EncryptionInstrumentation.Name))
    .WithMetrics(x => x.AddMeter(EncryptionInstrumentation.Name));
```

| Metric | Type | Tags |
|---|---|---|
| `efcore.encryption.values` | counter | `db.context`, `operation` (`encrypt`, `decrypt`) |
| `efcore.encryption.decryption.failures` | counter | `db.context`, `error.type` (`tampered`, `key_not_found`, `invalid_format`) |
| `efcore.encryption.key_wrapper.duration` | histogram (s) | `db.context`, `operation` (`generate`, `unwrap`, `rewrap`), `error.type` |
| `efcore.encryption.root_key.loads` | counter | `db.context`, `trigger` (`active`, `on_demand`, `refresh`), `result` |
| `efcore.encryption.reencryption.values` | counter | `db.context`, `result` (`reencrypted`, `skipped`, `invalid`) |

Traces cover key management service calls, root key loads, rotation, rewrapping, key usage and re-encryption; values are counted, not traced.

### Startup

Keys are loaded when the host starts, before hosted services run. Without a generic host call `await app.Services.InitializeEncryptionAsync()`, otherwise keys are loaded on first use. With SQLite load them before the first `SaveChanges`: it locks the whole database while saving, so creating the root key during the save waits for the save to finish.


