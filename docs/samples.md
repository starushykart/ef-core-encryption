---
title: Samples
nav_order: 11
---

# Samples
{: .no_toc }

The repository has two small ASP.NET Core apps you can run locally. They're the quickest way to see what actually ends up in the database and to try key rotation without touching a real system.

1. TOC
{:toc}

---

## Before you start

You'll need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Docker. From the repository root, start PostgreSQL and LocalStack (a local AWS emulator, used for KMS):

```bash
docker compose up -d
```

Both samples create their database on startup, so there's nothing else to set up.

## Static key sample

[`samples/EntityFrameworkCore.Samples.Encryption.Aes`](https://github.com/starushykart/ef-core-encryption/tree/main/samples/EntityFrameworkCore.Samples.Encryption.Aes)

This one uses a static key from `appsettings.json`. It shows most of the ways you can mark properties as encrypted, and [migrating a table encrypted with plain AES-256](#migrating-from-plain-aes-256). Its `Customer` entity has:

- `Name`, which is left as plain text so you can search and sort by it
- `Email`, encrypted with the `[Encrypted]` attribute
- `Phone`, encrypted with `IsEncrypted()` in `OnModelCreating`
- `Status` (an enum) and `BirthDate` (a `DateOnly`), converted to strings and then encrypted
- `Address`, a complex type where only `Street` is encrypted and `City` isn't
- `Passport`, a `byte[]` that's stored encrypted as binary

Run it:

```bash
cd samples/EntityFrameworkCore.Samples.Encryption.Aes
dotnet run
```

Create a customer:

```bash
curl -X POST http://localhost:5152/customers \
  -H "Content-Type: application/json" \
  -d '{"name":"Jane Doe","email":"jane@example.com","phone":"+1 555 0100","birthDate":"1990-04-01","street":"1 Main St","city":"Springfield","passportScanBase64":"AQID"}'
```

Now look at the table. You'll see the name and the city as you entered them, while everything else is unreadable:

```bash
docker exec -it encryption-samples-postgres psql -U debug -d encryption-sample-aes \
  -c 'select "Name", "Email", "Address_City", "Address_Street" from "Customers"'
```

Reading through the API gives you the decrypted values back:

```bash
curl http://localhost:5152/customers?name=Jane
```

The email has a [blind index](blind-indexes), so you can look customers up by it. Case and surrounding spaces don't matter:

```bash
curl "http://localhost:5152/customers/by-email?email=%20JANE@example.com"
```

The phone doesn't have one, so the same kind of query is refused with a 400 and an explanation:

```bash
curl "http://localhost:5152/customers/by-phone?phone=%2B1%20555%200100"
```

If you look at the table again, you'll see the `Email_Index` column holding a hash rather than the email.

The health check is at `http://localhost:5152/health`, and in development the OpenAPI document is at `/openapi/v1.json`.

### Migrating from plain AES-256

The sample also has a `Contracts` table that plays the part of existing data. On first start it's filled the way a previous version of the app would have done it: IBANs encrypted with plain AES-256-CBC under an old key (`Encryption:LegacyKey`), without the library. The app reads them through a [legacy decryptor](migrating) until they're migrated.

The contracts read like any other data, old or new:

```bash
curl http://localhost:5152/contracts
curl -X POST "http://localhost:5152/contracts?number=C-004&iban=NL91ABNA0417164300"
```

Look at what's stored. The first three are the old format, the new one starts with `AQAB`, the library's header:

```bash
curl http://localhost:5152/contracts/stored
```

Key usage counts the old values as having no root key:

```bash
curl http://localhost:5152/keys/usage
```

Migrate them. This runs in batches and would be safe while the app is serving traffic:

```bash
curl -X POST http://localhost:5152/keys/re-encrypt
```

Check the usage and the stored values again: everything is in the library's format now. In a real app, this is the point where you'd remove `UseLegacyDecryptor` and retire the old key.

## AWS KMS sample

[`samples/EntityFrameworkCore.Samples.Encryption.AwsKms`](https://github.com/starushykart/ef-core-encryption/tree/main/samples/EntityFrameworkCore.Samples.Encryption.AwsKms)

This one uses AWS KMS (via LocalStack) and focuses on key management. On first start KMS generates a root key, and the library stores it, wrapped, in the `__EncryptionKeys` table. The sample also checks for rotated keys every five minutes and prints traces and metrics to the console with OpenTelemetry.

Run it:

```bash
cd samples/EntityFrameworkCore.Samples.Encryption.AwsKms
dotnet run
```

Watch the console as it starts: you'll see the `root_key.load` and `key_wrapper.generate` (or `unwrap`) activities from the startup key load.

Add a few values:

```bash
curl -X POST "http://localhost:5052/passwords?password=first"
curl -X POST "http://localhost:5052/passwords?password=second"
```

Then walk through a full rotation:

```bash
# 1. create a new root key; new values are encrypted with it from now on
curl -X POST http://localhost:5052/keys/rotate

# 2. see which keys your data uses: the existing values still use root key 1
curl http://localhost:5052/keys/usage

# 3. re-encrypt everything with the new key
curl -X POST http://localhost:5052/keys/re-encrypt

# 4. check again: only root key 2 is left
curl http://localhost:5052/keys/usage
```

To try moving to a different KMS key, create one in LocalStack, put its id into `Encryption:KmsKeyId` in `appsettings.json`, restart the sample and run:

```bash
docker exec db-encryption-localstack awslocal kms create-key
curl -X POST http://localhost:5052/keys/rewrap
```

The root keys are now wrapped by the new KMS key, and none of the stored values had to change.

## Cleaning up

```bash
docker compose down
```

LocalStack keeps its keys in `dev-env/localstack-data`. If you delete that folder, the KMS key used by the sample is recreated, but anything encrypted with the old one can't be read anymore. That's a good, harmless way to see what losing a key looks like.
