---
title: Home
layout: home
nav_order: 1
---

# EF Core Encrypted
{: .fs-9 }

Encrypt sensitive columns in your EF Core application. Mark a property as encrypted and keep writing EF Core code the way you always do.
{: .fs-6 .fw-300 }

[Get started](getting-started){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[View on GitHub](https://github.com/starushykart/ef-core-encryption){: .btn .fs-5 .mb-4 .mb-md-0 }

---

Values are encrypted with AES-256-GCM in your application before they're sent to the database, and decrypted again when entities are loaded. The database, its backups and anyone with access to them only see ciphertext.

```csharp
public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;

    [Encrypted]
    public string Email { get; set; } = null!;
}
```

That's what ends up in the table:

| Name       | Email |
|------------|-------|
| `Jane Doe` | `AQABAAAAAMtY9...r0Q==` |

## What you get

- **It stays out of your way.** Entities, queries and `SaveChanges` don't change.
- **Tampering is detected.** If someone edits a value in the database or copies it to another column, reading it fails instead of returning garbage.
- **More than strings.** You can encrypt `byte[]`, enums, dates, value objects and properties of complex types.
- **Pick where keys come from.** Use a [static key](key-sources#static-key) from your configuration, [AWS KMS](key-sources#aws-kms), or [your own key management service](key-sources#custom-key-management).
- **Rotate keys without downtime.** Old values stay readable, and you can [re-encrypt](key-management#re-encrypting-existing-values) them in the background.
- **Move existing data over without downtime.** Values encrypted by your previous code, or still in plaintext, stay readable while they're [migrated](migrating) in the background.
- **Search by encrypted values.** Add a [blind index](blind-indexes) and `Where(x => x.Email == email)` works, using a database index.
- **Mistakes in queries are caught.** A LINQ query that compares an encrypted column [throws](queries) instead of silently returning nothing.
- **Production ready.** A [health check and OpenTelemetry](observability) metrics and traces are built in.
- **Works with any relational database.** It's tested with PostgreSQL, SQL Server and SQLite.

## Packages

| Package | What it's for |
|---------|---------------|
| [`EntityFrameworkCore.Encrypted`](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted) | The library itself, static keys and custom key management |
| [`EntityFrameworkCore.Encrypted.AwsKms`](https://www.nuget.org/packages/EntityFrameworkCore.Encrypted.AwsKms) | AWS KMS support |

Both target .NET 10 and EF Core 10.

## Where to go next

- New here? Start with [Getting started](getting-started).
- Deciding how to manage keys? Read [Key sources](key-sources).
- Need to find rows by an encrypted value? See [Blind indexes](blind-indexes).
- Curious how it's built or what it protects against? See [How it works](how-it-works).
- Want to try it first? Run one of the [samples](samples).

{: .warning }
> If you lose the key (the static key, the KMS key or the `__EncryptionKeys` table), the data can't be decrypted anymore. The authors don't take responsibility for lost keys or data, so please test backups, restores and key rotation before you rely on it in production.
