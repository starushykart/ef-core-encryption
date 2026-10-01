---
title: Key management
nav_order: 5
---

# Key management
{: .no_toc }

Every encrypted value remembers which key encrypted it. That's what makes rotation painless: new values use the new key, and existing values keep working with the key they were written with. You don't need downtime, and you don't have to re-encrypt everything at once.

The library gives you four operations, all extension methods on `IServiceProvider`:

```csharp
await app.Services.GetKeyUsageAsync<AppDbContext>();     // which keys does my data use?
await app.Services.ReEncryptAsync<AppDbContext>();       // move existing data to the current key
await app.Services.RotateRootKeyAsync<AppDbContext>();   // create a new root key (key management service only)
await app.Services.RewrapRootKeysAsync<AppDbContext>();  // move root keys to another KMS key
```

You'd usually call them from an admin endpoint, a background job or a small one-off tool.

1. TOC
{:toc}

---

## At a glance

| I want to... | Do this |
|--------------|---------|
| rotate a static key | add the new key with a higher id, e.g. `UseKey(newKey, id: 2)` |
| rotate the root key with KMS | call `RotateRootKeyAsync` |
| rotate keys without any KMS calls | bump the data key version with `UseDataKeyVersion(n + 1)` |
| get rid of an old key | run `ReEncryptAsync`, then check `GetKeyUsageAsync` |
| switch to another KMS key | configure the new key, then call `RewrapRootKeysAsync` |
| rotate the KMS key material | turn on automatic rotation in KMS; nothing to do in the app |

## Rotating the root key

If you use a key management service, creating a new root key is a single call:

```csharp
int newRootKeyId = await app.Services.RotateRootKeyAsync<AppDbContext>();
```

The new key is generated, stored in `__EncryptionKeys`, and used by this instance for new values right away. Everything encrypted before stays readable.

With a static key there's nothing to generate. You add a new key with a higher id instead, and `RotateRootKeyAsync` throws to remind you.

### What about other instances?

When you run several instances, the others pick up the new root key in one of three ways:

- the first time they read a value encrypted with it, since unknown keys are loaded on demand
- when they restart
- on a schedule, if you turn on refresh:

```csharp
builder.Services.AddEncryption(x => x
    .UseAwsKms(keyId)
    .RefreshRootKeysEvery(TimeSpan.FromMinutes(5)));
```

A refresh is just a quick read from the key store. KMS is only called when a new key actually shows up. Without a refresh, an instance that only writes keeps using the previous key until it restarts. That's safe, since both keys stay valid, but it delays the rotation.

## Rotating the data key

Values aren't encrypted with the root key directly. They're encrypted with a data key derived from it, and data keys have versions. Bumping the version gives you a fresh key without calling KMS and without storing anything new:

```csharp
builder.Services.AddEncryption(x => x.UseAwsKms(keyId).UseDataKeyVersion(1));   // the default is 0
```

Roll it out to all instances, and values written with older versions keep working.

## Key usage

Before you retire a key, you want to know nothing still depends on it. `GetKeyUsageAsync` counts the stored values per table, column, root key and data key version:

```csharp
IReadOnlyList<KeyUsage> usage = await app.Services.GetKeyUsageAsync<AppDbContext>();
```

```json
[
  { "table": "Customers", "column": "Email", "rootKeyId": 1, "dataKeyVersion": 0, "values": 1200 },
  { "table": "Customers", "column": "Email", "rootKeyId": 2, "dataKeyVersion": 0, "values": 35 },
  { "table": "Customers", "column": "Phone", "rootKeyId": null, "dataKeyVersion": null, "values": 3 }
]
```

If a key isn't in the list, no value uses it. An empty root key id means values that aren't encrypted at all, typically plaintext from before the column was encrypted. `NULL`s aren't counted.

It reads every encrypted column in every table, so on a large database you'll want to run it outside of peak hours.

## Re-encrypting existing values

`ReEncryptAsync` goes through all encrypted columns and re-encrypts every value that doesn't use the current root key and data key version:

```csharp
ReEncryptionResult result = await app.Services.ReEncryptAsync<AppDbContext>(batchSize: 1000);
```

The result tells you how many values were re-encrypted, how many were skipped and how many were invalid.

It's designed to run while your app is serving traffic:

- **It works in small batches.** Each batch of `batchSize` values gets its own transaction.
- **It never overwrites newer data.** If your app updates a value while re-encryption is running, that update wins. It's counted as skipped, because your app already encrypted it with the current key.
- **You can stop and restart it.** Values that are already done are skipped next time.
- **Bad values are left alone.** Anything that can't be decrypted is counted as invalid and left untouched.
- **Transient errors are retried**, using your context's execution strategy.

## Retiring an old key

Putting it all together:

1. Rotate, so a new key (a new root key, a new static key or a new data key version) encrypts new values.
2. Make sure every instance uses it, either by restarting or by waiting for the refresh interval.
3. Run `ReEncryptAsync`.
4. Check `GetKeyUsageAsync` and confirm the old key is gone from the list.
5. With a static key, remove the old one from your configuration. With KMS, the old wrapped root key can just stay in `__EncryptionKeys`, unused.

## Moving to another KMS key

Sometimes you need to move to a different KMS key: a new account, a multi-region key, or a key with a stricter policy. You can do that without touching a single stored value:

1. Allow `kms:ReEncryptFrom` on the old key and `kms:ReEncryptTo` on the new one.
2. Point the app at the new key with `UseAwsKms(newKeyId)`. From now on, new root keys are wrapped with it.
3. Rewrap the existing root keys:

   ```csharp
   int rewrapped = await app.Services.RewrapRootKeysAsync<AppDbContext>();
   ```

   KMS re-encrypts them internally with `ReEncrypt`, so the plaintext root key never leaves KMS.
4. Keep the old key enabled until that's done, then disable it.

Your data doesn't change at all, because it's encrypted with the root keys, and those stay the same. Only their wrapping changes.

## Example: admin endpoints

```csharp
var keys = app.MapGroup("/admin/keys").RequireAuthorization("admin");

keys.MapPost("/rotate", (IServiceProvider services, CancellationToken ct)
    => services.RotateRootKeyAsync<AppDbContext>(ct));

keys.MapGet("/usage", (IServiceProvider services, CancellationToken ct)
    => services.GetKeyUsageAsync<AppDbContext>(ct));

keys.MapPost("/re-encrypt", (IServiceProvider services, CancellationToken ct)
    => services.ReEncryptAsync<AppDbContext>(batchSize: 500, cancellationToken: ct));

keys.MapPost("/rewrap", (IServiceProvider services, CancellationToken ct)
    => services.RewrapRootKeysAsync<AppDbContext>(ct));
```

For big tables, run re-encryption as a background job rather than inside an HTTP request.
