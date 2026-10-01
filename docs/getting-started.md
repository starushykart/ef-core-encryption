---
title: Getting started
nav_order: 2
---

# Getting started
{: .no_toc }

This page takes you from an empty project to your first encrypted column. It uses a static key, because that's the quickest to set up. You can switch to a key management service later without changing your entities.

1. TOC
{:toc}

---

## Install the package

```bash
dotnet add package EntityFrameworkCore.Encrypted
```

If you're going to use AWS KMS, install `EntityFrameworkCore.Encrypted.AwsKms` instead. It brings in the core package.

## Create a key

The static key is a random 32-byte AES-256 key, encoded as Base64. You can generate one with a single line of C#:

```csharp
Console.WriteLine(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
```

Treat it like a database password. In development, [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) work well:

```bash
dotnet user-secrets set "Encryption:Key" "<your key>"
```

In production, keep it in a secret store such as AWS Secrets Manager or Azure Key Vault, or pass it in as an environment variable. Never commit it to source control.

## Register encryption

Call `AddEncryption` with your key, and enable encryption on the context with `UseEncryption()`:

```csharp
using EntityFrameworkCore.Encrypted;

builder.Services
    .AddEncryption(x => x.UseKey(builder.Configuration["Encryption:Key"]!))
    .AddDbContext<AppDbContext>(x => x
        .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
        .UseEncryption());
```

This works the same way with `AddDbContextPool`, `AddDbContextFactory` and `AddPooledDbContextFactory`.

## Mark what should be encrypted

Use the `[Encrypted]` attribute, or `IsEncrypted()` if you prefer configuring the model in `OnModelCreating`:

```csharp
using EntityFrameworkCore.Encrypted.Annotations;

public class Customer
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    [Encrypted]
    public string Email { get; set; } = null!;

    public string? Phone { get; set; }
}

protected override void OnModelCreating(ModelBuilder modelBuilder)
    => modelBuilder.Entity<Customer>().Property(x => x.Phone).IsEncrypted();
```

Enums, dates, binary data and complex types are covered in [Encrypting properties](encrypting-properties).

## Add a migration

```bash
dotnet ef migrations add AddEncryption
dotnet ef database update
```

You'll notice two things in the migration. Encrypted strings become `text` columns, because they're stored as Base64, and binary values stay binary. There's also a new `__EncryptionKeys` table, which is where wrapped keys live once you use a key management service.

If you have a design-time factory, it only needs to call `UseEncryption()` too. The design-time tools don't need any keys:

```csharp
internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql().UseEncryption().Options);
}
```

{: .note }
Encrypted columns can't have a maximum length, so `[MaxLength]`, `HasMaxLength` and types like `varchar(100)` won't work on them. The limit would apply to the ciphertext rather than to your value. If you need a limit, validate it in your application.

## That's it

Your code doesn't change:

```csharp
context.Customers.Add(new Customer { Id = Guid.NewGuid(), Name = "Jane", Email = "jane@example.com" });
await context.SaveChangesAsync();

var customer = await context.Customers.FirstAsync(x => x.Name == "Jane");
Console.WriteLine(customer.Email);   // jane@example.com
```

The one thing you can't do is filter or sort by an encrypted column in the database, because the database only has ciphertext. [Queries](queries) explains what to do instead.

## A note on startup

The library loads keys once, when the host starts, so your first request doesn't have to wait for them. If the database isn't migrated yet at that point (say, because migrations run a bit later during startup), it simply loads them on first use.

Console apps and tests often don't use a generic host. In that case, load the keys yourself:

```csharp
await serviceProvider.InitializeEncryptionAsync();
```

{: .important }
If you use SQLite, always call `InitializeEncryptionAsync` before the first `SaveChanges`. SQLite locks the entire database while saving, so creating the first key in the middle of a save would wait forever.
