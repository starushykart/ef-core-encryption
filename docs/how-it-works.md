---
title: How it works
nav_order: 10
---

# How it works
{: .no_toc }

You don't need any of this to use the library. But if you're reviewing it for a security assessment, or you're just curious, this is what happens under the hood.

1. TOC
{:toc}

---

## The key hierarchy

There are three layers of keys:

```
KMS key ──wraps──► root key ──HKDF──► data key ──AES-256-GCM──► value
```

- **The wrapping key** lives in your key management service, for example a KMS key, and never leaves it. With a static key there's no wrapping key, because your configured key is the root key.
- **The root key** is what everything else derives from. With KMS there's one per context, and it's stored wrapped in that context's `__EncryptionKeys` table. In plaintext it only ever exists in your app's memory.
- **The data key** is derived from the root key with HKDF-SHA256 (with `efenc:dek:v{version}` as the info). It's never stored anywhere, which is why rotating it costs nothing.

[Blind indexes](blind-indexes) use a separate **index key** per context. It's stored in `__EncryptionKeys` with the reserved id `0`, wrapped by your key management service, or with a key derived from the active static key (HKDF, `efenc:wrap:index-key`). Each column's HMAC key is derived from it with HKDF (`efenc:bidx:{label}`). Because it's independent of the root keys, rotating them doesn't change any index.

## What happens at runtime

1. **At startup**, the library loads the active root key for each encrypted context. With a key management service, it reads the wrapped keys from `__EncryptionKeys`. If there aren't any, it generates one; when several instances start at once, only one of them succeeds and the others use its key. Unwrapping takes one call per context.
2. **When you save**, values are encrypted with the data key of the active root key and data key version.
3. **When you read**, each value carries the id of the key that encrypted it. If that's an older root key, it's loaded the first time it's needed and then cached.
4. **Optionally**, the key store is checked every so often for a root key rotated by another instance.
5. **On shutdown**, all keys are wiped from memory.

## What a stored value looks like

```
[format 1B][root key id 2B][data key version 4B][nonce 12B][ciphertext][tag 16B]
```

- The **format** byte (currently `1`) leaves room to change the layout later without breaking stored data.
- The **root key id** and **data key version** say which key encrypted the value, so values written with older keys stay readable.
- The **nonce** is 12 random bytes, new for every value.
- The **ciphertext** is exactly as long as the plaintext (UTF-8 for strings).
- The **tag** is the AES-GCM authentication tag.

So every value grows by 35 bytes, and strings grow by about another third when they're Base64 encoded. A 20-character email, for example, ends up as 76 characters.

The header and the column label are authenticated as associated data. Changing any byte (including the key id) or moving the value to a different column makes decryption fail.

## What it protects against

Encryption at the application level protects you from:

- anyone who gets hold of the database, a backup, a replica, an export or SQL logs, but not the key
- database administrators and anyone else who only has database access
- values being modified, because tampering is detected when the value is read
- values being copied between columns or tables, such as moving an encrypted salary into a field an attacker can read

It doesn't protect you from:

- **someone who controls your application.** It has the keys and decrypts values as part of its job.
- **values swapped between rows of the same column.** The label ties a value to its column, not to its row.
- **equality patterns in blind-indexed columns.** A [blind index](blind-indexes#what-it-reveals) shows which rows share a value.
- **metadata.** Which values are `NULL`, how long they are (the ciphertext length gives away the plaintext length), how many rows there are, and everything in columns you didn't encrypt.
- **a leaked static key.** If that's a concern, use a key management service, which keeps the root key out of your configuration entirely.

With AWS KMS, the plaintext root key only exists inside KMS and in your app's memory. The database holds nothing but wrapped keys, so a stolen backup on its own is useless. You can revoke access to KMS at any time, see every use in CloudTrail and restrict it with IAM.

## How it plugs into EF Core

It's built entirely on EF Core's public extension points:

- A **model convention** finds encrypted properties, adds value converters (after any conversions you configured), adds the `__EncryptionKeys` entity and checks the model, for example for unsupported types or maximum lengths.
- A **query expression interceptor** stops queries that would compare encrypted columns in the database.
- **Hosted services** load the keys at startup and refresh them.

The EF Core model is cached per application service provider. That means several apps with different keys can run in one process, which matters for integration tests.
