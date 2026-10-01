---
title: Troubleshooting
nav_order: 10
---

# Troubleshooting
{: .no_toc }

These are the errors people run into most often, what causes them and how to fix them. All of them are thrown as `EntityFrameworkEncryptionException`.

1. TOC
{:toc}

---

## "... is encrypted and can't be compared, searched, sorted or grouped in a query"

A query tries to filter, sort or group by an encrypted column in the database. That can't work, because the database only sees random ciphertext. Filter by other columns and check the encrypted value in memory, or add a hash column for lookups. [Queries](queries) explains both.

## "... is encrypted and can't have a maximum length"

The property has `[MaxLength]`, `HasMaxLength` or a sized column type such as `varchar(100)`. The limit would apply to the ciphertext, which is longer than the value, so the library rejects it. Remove the limit and validate the length in your application instead.

## "... only string and byte[] values can be encrypted"

The property has some other type, for example an enum, a date or a number. Convert it first and then mark it as encrypted:

```csharp
e.Property(x => x.Status).HasConversion<string>().IsEncrypted();
```

## "Can't decrypt value of '...': it was modified or encrypted for another column"

The stored value failed authentication. Usually it's one of these:

- **The column or table was renamed.** Values are bound to their `"{table}.{column}"` label, so after a rename the label no longer matches. Set the old label explicitly with `IsEncrypted("Customers.Email")`; see [Labels](encrypting-properties#labels).
- **The value was copied from another column**, for example by a data migration script. Copy the plaintext through the application instead.
- **Someone changed the value in the database.** This is exactly what authenticated encryption is meant to catch.
- **A different key with the same id is configured**, for example a new static key that reuses id 1. Give each new key a new id.

## "Root key ... not found"

A value was encrypted with a root key the application doesn't know about:

- With a static key, the old key was removed from the configuration while some values still use it. Put it back, run [`ReEncryptAsync`](key-management#re-encrypting-existing-values), and only remove it once [`GetKeyUsageAsync`](key-management#key-usage) no longer lists it.
- With a key management service, the row is missing from `__EncryptionKeys`. That can happen if the database was restored from a backup older than a key rotation, or if the context points at a different database than the one that wrote the data.

## "Encrypted value is corrupted or has an unsupported format"

The column contains something that isn't an encrypted value, usually plaintext written before the column was marked as encrypted. [Encrypting a column that already has data](encrypting-properties#encrypting-a-column-that-already-has-data) shows how to migrate it.

## "Encryption services are not registered"

`UseEncryption()` was called on a context, but `AddEncryption(...)` wasn't called on the service collection. Register both:

```csharp
builder.Services
    .AddEncryption(x => x.UseKey(key))
    .AddDbContext<AppDbContext>(x => x.UseNpgsql(connectionString).UseEncryption());
```

## "Root key is not configured" / "Only one root key source can be configured"

`AddEncryption` needs exactly one key source: `UseKey(...)`, `UseAwsKms(...)` or `UseKeyWrapper(...)`. You can't mix a static key with a key management service.

## Encrypting fails in a context created with `new`

A context built from options you created yourself, outside of dependency injection (a design-time factory, for example), knows which columns are encrypted, so migrations work. It has no keys, though, so saving or reading encrypted values throws. Resolve the context from the service provider instead.

## The first `SaveChanges` hangs with SQLite

SQLite locks the whole database while it saves. If the root key doesn't exist yet, the library tries to create it inside that save and ends up waiting for it. Call `await app.Services.InitializeEncryptionAsync()` before the first save. Inside a generic host this happens automatically on startup.

## Startup fails with "Can't read __EncryptionKeys ... Make sure the database is migrated"

The key table doesn't exist yet. Add a migration after enabling encryption. If migrations run during startup, the library notices the missing table and loads keys on first use instead, so you'll only see this error at runtime when the database really isn't migrated.

## Limitations

- **Relational providers only.** The library relies on EF Core's relational model; Cosmos DB and the in-memory provider aren't supported.
- **No searching in the database.** That's the trade-off of random nonces; see [Queries](queries).
- **No row binding.** A value can't be moved to another column, but it can be swapped with the value of the same column in another row.
- **Raw SQL isn't checked.** `FromSql` and `ExecuteSql` run as written, and raw SQL sees ciphertext.
