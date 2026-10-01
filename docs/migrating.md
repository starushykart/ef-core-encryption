---
title: Migrating existing data
nav_order: 8
---

# Migrating existing data
{: .no_toc }

Maybe your database already has encrypted columns, written by your own code with your own key. Or the columns you want to protect are still in plaintext. Either way, you can move to the library without downtime and without a big-bang migration script.

1. TOC
{:toc}

---

## How it works

You tell the library how to read the old values. From then on:

1. **Reading works right away.** Values in the library's format are decrypted as usual. Anything else goes to your legacy decryptor, so the app sees plaintext for both old and new rows.
2. **New writes use the library's format.** Every entity you save migrates its values as a side effect.
3. **The rest is migrated in the background.** `ReEncryptAsync` rewrites all remaining old values in batches while the app keeps running.
4. **You remove the legacy decryptor** once nothing old is left.

That's one deployment plus a background job.

## Values encrypted by your previous code

Implement `ILegacyDecryptor`. It receives the value exactly as it's stored in the column, and returns the plaintext, or `null` if the value isn't in your format:

```csharp
public sealed class OldDecryptor(IConfiguration configuration) : ILegacyDecryptor
{
    private readonly byte[] _oldKey = Convert.FromBase64String(configuration["OldEncryption:Key"]!);

    // text columns: e.g. your Base64 ciphertext
    public string? Decrypt(string storedValue, LegacyValueContext context)
        => MyOldCrypto.TryDecrypt(storedValue, _oldKey);

    // binary columns, if you have any
    public byte[]? Decrypt(byte[] storedValue, LegacyValueContext context)
        => MyOldCrypto.TryDecrypt(storedValue, _oldKey);
}
```

Register it next to your key source:

```csharp
builder.Services.AddEncryption(x => x
    .UseAwsKms(keyId)
    .UseLegacyDecryptor<OldDecryptor>());
```

It's a singleton and can take dependencies from the container. You only need to implement the methods for the column types you have.

`context.Label` tells you which column a value comes from (`"Customers.Email"`), in case you used different keys for different columns.

For converted properties, return the value as it was stored before encryption. For an enum stored as text that's `"Active"`, not the enum; the library applies your conversion afterwards.

### Example: plain AES-256

A common setup: AES-256-CBC with a random IV, stored as Base64 of the IV followed by the ciphertext. This is also how version 1.x of this library stored values, so you can use it to upgrade from 1.x.

```csharp
public sealed class Aes256CbcLegacyDecryptor(IConfiguration configuration) : ILegacyDecryptor
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);
    private readonly byte[] _key = Convert.FromBase64String(configuration["Encryption:LegacyKey"]!);

    public string? Decrypt(string storedValue, LegacyValueContext context)
    {
        var buffer = new byte[storedValue.Length];

        // the IV plus at least one block, whole blocks only
        if (!Convert.TryFromBase64String(storedValue, buffer, out var length) || length < 32 || length % 16 != 0)
            return null;

        try
        {
            using var aes = Aes.Create();
            aes.Key = _key;
            return StrictUtf8.GetString(aes.DecryptCbc(buffer.AsSpan(16, length - 16), buffer.AsSpan(0, 16)));
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return null;   // not this format, or another key
        }
    }
}
```

The [static key sample](samples#migrating-from-plain-aes-256) runs this migration end to end.

{: .note }
CBC isn't authenticated, so the decryptor can't be completely sure a value is its own. The padding and UTF-8 checks reject almost everything else, and values that look like the library's format are always decrypted by the library first.

## Columns that were stored in plaintext

There's a built-in decryptor for that:

```csharp
builder.Services.AddEncryption(x => x
    .UseKey(builder.Configuration["Encryption:Key"]!)
    .UseLegacyDecryptor(LegacyDecryptor.Plaintext));
```

Mark the properties as encrypted, deploy, and run `ReEncryptAsync`. The plaintext values are encrypted in place.

## Step by step

1. **Check the columns.** The library stores strings as text and binary values as binary, without a maximum length. If an old column is something like `varchar(200)`, the migration EF generates widens it. Encrypted values are longer than the old ones, so that has to happen first.
2. **Mark the properties as encrypted** and add a migration. It also adds the `__EncryptionKeys` table and any [blind index](blind-indexes) columns.
3. **Deploy with `UseLegacyDecryptor`.** The app reads old and new values from now on.
4. **Run the migration job:**

   ```csharp
   ReEncryptionResult result = await app.Services.ReEncryptAsync<AppDbContext>();
   ```

   It works in batches and is safe to run while the app is serving traffic. If the app updates a row in the meantime, the app's write wins. You can stop it and start it again. If a column has a blind index, its index is filled for every migrated value too.
5. **Check that nothing is left:**

   ```csharp
   var usage = await app.Services.GetKeyUsageAsync<AppDbContext>();
   var remaining = usage.Where(x => x.RootKeyId == null).Sum(x => x.Values);   // 0 when done
   ```

6. **Remove `UseLegacyDecryptor`** in the next deployment, and retire the old key.

The `efcore.encryption.legacy.values` metric counts values read through the legacy decryptor. It's a nice way to watch the migration finish in production.

## How the formats are told apart

Every value in the library's format starts with a small header: a format byte, the key id and the data key version. A value counts as the library's format only if that header is there, with a root key id of at least 1 and a data key version you've configured. If its data key version is newer (a newer deployment rolling out), the library tries to decrypt it first and only asks your decryptor if that fails. Everything else is passed to your decryptor. Old ciphertext has random bytes in those positions, so it's mistaken for the library's format about once in 2⁴⁰ values.

One rule stays strict: a value in the library's format with a configured data key version that fails to decrypt is never handed to the legacy decryptor, so tampering is still reported as tampering. The plaintext decryptor can't tell a value with a damaged header from plaintext, though, and returns it as stored. Remove it as soon as the migration is done.

## Things to keep in mind

- **Blind index lookups don't find old rows yet.** They have no index until they're migrated, so run the migration job before you rely on lookups.
- **Queries on encrypted columns are rejected from the start**, the same as after the migration. If your app filtered on these columns before, change those queries first. Use a blind index or other columns.
- **Your decryptor should return `null` for values it doesn't recognize**, rather than throw. Errors from it are reported with the column's label.
