---
title: Troubleshooting
nav_order: 12
---

# Troubleshooting
{: .no_toc }

These are the errors people run into most often, what causes them and how to fix them. All of them are thrown as `EntityFrameworkEncryptionException`.

1. TOC
{:toc}

---

## "... is encrypted and can't be compared, searched, sorted or grouped in a query"

A query tries to filter, sort or group by an encrypted column in the database. That can't work, because the database only sees random ciphertext. Filter by other columns and check the encrypted value in memory, or add a hash column for lookups. [Queries](queries) explains both.

## "... has a blind index but isn't encrypted"

`[BlindIndex]` or `HasBlindIndex()` is only for encrypted properties. Add `[Encrypted]` or `IsEncrypted()` as well.

## "... has a blind index and can only be set to a value in ExecuteUpdate"

`ExecuteUpdate` tried to set a blind-indexed property from another column, for example `s.SetProperty(x => x.Email, x => x.Login)`. The database can't compute the hash, so set it to a value, or load the entities and use `SaveChanges`.

## A blind index lookup doesn't find existing rows

Rows written before you added the blind index, or before you changed its normalization, still have an empty or outdated index. Run [`RebuildBlindIndexesAsync`](blind-indexes#adding-an-index-to-existing-data).

## "Blind index key ... is wrapped with static key N, which is no longer configured"

You removed an old static key before the app had started once with both the old and the new key, so the blind index key couldn't be rewrapped. Add the old key back, start the app once, then remove it.

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

The column contains something that isn't an encrypted value. Usually that's plaintext written before the column was marked as encrypted, or a value encrypted by your previous code. [Migrating existing data](migrating) shows how to read those values and migrate them. If you already use a legacy decryptor, it returned `null` for this value, so check that it recognizes all of your old formats.

## "Legacy decryptor failed to decrypt value of '...'"

Your `ILegacyDecryptor` threw for a value that isn't in the library's format. The inner exception has the details. Return `null` instead of throwing for values your decryptor doesn't recognize.

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

## "... is part of a key or foreign key and can't be encrypted"

Also: "... is a concurrency token", "... can't have a unique index" and "... has seed data (HasData) for encrypted properties". These mappings need the database to compare stored values, and encrypted values differ every time. [What can't be encrypted](encrypting-properties#what-cant-be-encrypted) has the alternatives.

## "The model of ... wasn't built with encryption"

The context uses `UseModel(...)`, for example a compiled model from `dotnet ef dbcontext optimize`. Encrypted properties are configured while the model is built, so remove `UseModel` for contexts with encryption.

## "... is encrypted and can't be copied to another column in ExecuteUpdate"

`ExecuteUpdate` sets a column from an encrypted one, or an encrypted column from another column. The database can't encrypt or decrypt, so load the entities and use `SaveChanges`.

## "... is configured with a shared DbConnection instance"

`ReEncryptAsync`, `GetKeyUsageAsync` and `RebuildBlindIndexesAsync` read on one connection while they write on another. Configure the context with a connection string instead of a `DbConnection` instance.

## "... already has the maximum number of root keys"

Root key ids are stored in two bytes of every value, so a context can have up to 65,535 root keys. That's far more than regular rotation needs. If you got here through automation that rotates too often, rotate less often, or rotate the data key version instead.

## Limitations

- **Relational providers only.** The library relies on EF Core's relational model; Cosmos DB and the in-memory provider aren't supported.
- **No searching in the database beyond equality.** That's the trade-off of random nonces. [Blind indexes](blind-indexes) cover equality lookups, and [Queries](queries) covers the rest.
- **No row binding.** A value can't be moved to another column, but it can be swapped with the value of the same column in another row.
- **Raw SQL isn't checked.** `FromSql` and `ExecuteSql` run as written, and raw SQL sees ciphertext.
- **Keys are per context type.** Every context type has its own root keys and blind index key, read from the database the context type connects to through dependency injection. A database per tenant behind one context type isn't supported: all tenants would share the keys found in one of them. Use one database for the key store, or a custom `IRootKeyStore`, and expect every tenant to share the same keys.
