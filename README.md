# Entity Framework Core Encrypted

[![Build and Test](https://github.com/starushykart/ef-core-encryption/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/build-and-test.yml)
[![CodeQL](https://github.com/starushykart/ef-core-encryption/actions/workflows/github-code-scanning/codeql/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/github-code-scanning/codeql)
[![Qodana](https://github.com/starushykart/ef-core-encryption/actions/workflows/code_quality.yml/badge.svg)](https://github.com/starushykart/ef-core-encryption/actions/workflows/code_quality.yml)
[![codecov](https://codecov.io/github/starushykart/ef-core-encryption/graph/badge.svg?token=C1JOFN38GC)](https://codecov.io/github/starushykart/ef-core-encryption)

Encrypt sensitive columns in your EF Core application. You mark a property with `[Encrypted]` and keep writing EF Core code the way you always do. Values are encrypted with AES-256-GCM before they're sent to the database and decrypted when entities are loaded, so the database and its backups only ever see ciphertext.

**[Read the documentation](https://starushykart.github.io/ef-core-encryption)**

| Package | NuGet |
|---|---|
| `EntityFrameworkCore.Encrypted` | [![NuGet](https://img.shields.io/nuget/vpre/EntityFrameworkCore.Encrypted)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted) |
| `EntityFrameworkCore.Encrypted.AwsKms` | [![NuGet](https://img.shields.io/nuget/vpre/EntityFrameworkCore.Encrypted.AwsKms)](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted.AwsKms) |

## Why you might like it

- **It stays out of your way.** Entities, queries and `SaveChanges` don't change.
- **Tampering is detected.** A value edited in the database, or copied into another column, fails to decrypt instead of returning garbage.
- **More than strings.** It handles `byte[]`, enums, dates, value objects and properties of complex types.
- **You choose where keys come from.** Use a static key from your configuration, AWS KMS, or plug in your own key management service.
- **Keys can be rotated without downtime.** Old values stay readable, and you can re-encrypt them in the background.
- **Existing data can move over without downtime.** Values encrypted by your previous code, or still in plaintext, stay readable while they're migrated in the background.
- **You can still look rows up by encrypted values.** Add a blind index and `Where(x => x.Email == email)` works, using a database index.
- **Query mistakes are caught.** A LINQ query that compares an encrypted column throws instead of silently returning nothing.
- **It's ready for production.** A health check and OpenTelemetry metrics and traces are built in.
- **It works with any relational database.** It's tested with PostgreSQL, SQL Server and SQLite.

## Documentation

- [Getting started](https://starushykart.github.io/ef-core-encryption/getting-started): install, configure and add a migration in a few minutes
- [Encrypting properties](https://starushykart.github.io/ef-core-encryption/encrypting-properties): strings, binary data, enums, dates, value objects and complex types
- [Key sources](https://starushykart.github.io/ef-core-encryption/key-sources): a static key, AWS KMS, or your own key management service
- [Key management](https://starushykart.github.io/ef-core-encryption/key-management): rotation, re-encryption and moving to another KMS key
- [Queries](https://starushykart.github.io/ef-core-encryption/queries): what you can and can't query on encrypted columns
- [Blind indexes](https://starushykart.github.io/ef-core-encryption/blind-indexes): looking up rows by an encrypted value
- [Migrating existing data](https://starushykart.github.io/ef-core-encryption/migrating): from your own encryption, or from plaintext, without downtime
- [Health check and OpenTelemetry](https://starushykart.github.io/ef-core-encryption/observability)
- [How it works](https://starushykart.github.io/ef-core-encryption/how-it-works): the key hierarchy, the value format and what it protects against
- [Troubleshooting](https://starushykart.github.io/ef-core-encryption/troubleshooting)

## Samples

There are two small ASP.NET Core apps in [`samples`](samples). The [static key sample](samples/EntityFrameworkCore.Samples.Encryption.Aes) shows the different kinds of encrypted properties, how querying works and a blind index. The [AWS KMS sample](samples/EntityFrameworkCore.Samples.Encryption.AwsKms) walks through key rotation, re-encryption and OpenTelemetry. Start PostgreSQL and LocalStack with `docker compose up -d`, then `dotnet run` in either of them. The [samples page](https://starushykart.github.io/ef-core-encryption/samples) has step-by-step walkthroughs.

## Building from source

You'll need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Docker, because the tests use Testcontainers. Then run `dotnet build` and `dotnet test`.

## A word of caution

If you lose the key (the static key, the KMS key or the `__EncryptionKeys` table), the data can't be decrypted anymore. The authors don't take responsibility for lost keys or data, so please test backups, restores and key rotation before you rely on it in production.

Please report security issues as described in [SECURITY.md](SECURITY.md). The project is licensed under [MIT](LICENSE).
