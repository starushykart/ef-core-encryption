---
title: Encrypting properties
nav_order: 3
---

# Encrypting properties
{: .no_toc }

1. TOC
{:toc}

---

## Attribute or fluent API

You can mark a property as encrypted in two ways, and they do exactly the same thing. Use whichever matches how you configure the rest of your model.

```csharp
public class Customer
{
    [Encrypted]
    public string Email { get; set; } = null!;

    public string? Phone { get; set; }
}

modelBuilder.Entity<Customer>().Property(x => x.Phone).IsEncrypted();
```

Nullable properties stay nullable. A `null` is stored as `NULL`, and only actual values are encrypted.

## What can be encrypted

At its core the library encrypts `string` and `byte[]`. Anything that EF Core can convert to one of those can be encrypted too.

| Property type | How it's stored | How to configure it |
|---------------|-----------------|---------------------|
| `string` | Base64 text (`text`, `nvarchar(max)`) | `[Encrypted]` or `IsEncrypted()` |
| `byte[]` | binary (`bytea`, `varbinary(max)`, `BLOB`) | `[Encrypted]` or `IsEncrypted()` |
| enums, dates, numbers, `Guid` | Base64 text | `HasConversion<string>().IsEncrypted()` |
| your own value objects | Base64 text or binary | `HasConversion(...).IsEncrypted()` |
| properties of complex types | as above | `[Encrypted]` inside the complex type |

Your conversion always runs first, and its result is what gets encrypted. Any of these, except properties of complex types, can also get a [blind index](blind-indexes), so you can look rows up by them.

### Enums, dates and numbers

```csharp
modelBuilder.Entity<Customer>(e =>
{
    e.Property(x => x.Status).HasConversion<string>().IsEncrypted();     // enum
    e.Property(x => x.BirthDate).HasConversion<string>().IsEncrypted();  // DateOnly
    e.Property(x => x.Salary).HasConversion<string>().IsEncrypted();     // decimal
});
```

### Value objects

```csharp
public readonly record struct TaxId(string Value);

modelBuilder.Entity<Customer>()
    .Property(x => x.TaxId)
    .HasConversion(x => x.Value, x => new TaxId(x))
    .IsEncrypted();
```

### Binary data

```csharp
public class Customer
{
    [Encrypted]
    public byte[]? Passport { get; set; }
}
```

Byte arrays are compared by their content, so EF Core notices when you change a single byte and saves it.

### Complex types

Put `[Encrypted]` on the properties inside the complex type that need it:

```csharp
public class Address
{
    [Encrypted]
    public string Street { get; set; } = null!;

    public string City { get; set; } = null!;
}

modelBuilder.Entity<Customer>().ComplexProperty(x => x.Address);
```

Or configure it fluently:

```csharp
modelBuilder.Entity<Customer>().ComplexProperty(x => x.Address, a => a.Property(x => x.Street).IsEncrypted());
```

Only `Street` is encrypted here. `City` stays readable, so you can still filter by it.

## Labels

Every encrypted value is tied to the column it belongs to. Behind the scenes the library uses a label like `Customers.Email` and authenticates it together with the value. If someone copies an encrypted value into another column, maybe one they're allowed to see, it won't decrypt there.

The flip side is that renaming a table or column changes the label, and existing values stop decrypting. When you rename, keep the old label:

```csharp
// the column used to be called "Email"
e.Property(x => x.EmailAddress).IsEncrypted("Customers.Email");

// or with the attribute
[Encrypted(Label = "Customers.Email")]
public string EmailAddress { get; set; } = null!;
```

{: .tip }
If you expect to rename things, set explicit labels like `"customers.email"` from day one. Then you never have to think about it.

Labels follow the mapping:

- With entity splitting (`SplitToTable`), a property gets the table it's moved to: `CustomerDetails.Email`.
- With TPC, a property declared on the base type is stored in every concrete table but has one label, from the base type's table, or `Base.Property` when the base type is abstract. Values of that property can be moved between those tables. If that matters to you, declare the property on each concrete type instead. Making an abstract base type concrete later changes the label, so set an explicit label first.

## Why there's no maximum length

Putting `[MaxLength]`, `HasMaxLength` or a sized column type like `varchar(100)` on an encrypted property throws when the model is built. That's deliberate. The ciphertext is 35 bytes longer than your value, and strings grow by another third when they're Base64 encoded, so the limit would cut off ciphertext at unexpected points. Validate the length of the value in your application instead.

## What can't be encrypted

The same value encrypts differently every time. A few things depend on the database comparing stored values, so those can't be encrypted, and they're rejected when the model is built:

- **Keys and foreign keys.** Rows couldn't be found or referenced. Keep identifiers plain, or use a surrogate key.
- **Concurrency tokens.** The stored value changes on every save, so every update would look like a conflict.
- **Columns with a unique index.** Uniqueness wouldn't be enforced. To keep values unique, check with a [blind index](blind-indexes) lookup before saving.
- **Seed data (`HasData`).** It's written into migrations without keys, and differently each time. Seed encrypted values from code on startup instead.
- **A column shared by an encrypted and a plain property**, for example two types in a TPH hierarchy mapping different properties to one column. Encrypt both properties, or give them their own columns.

A model passed with `UseModel(...)` isn't supported either. That includes compiled models from `dotnet ef dbcontext optimize`. Encrypted properties are configured while the model is built, so the first query or save throws instead of storing plaintext.

## Encrypting a column that already has data

Marking an existing column as encrypted doesn't encrypt the rows that are already there. Those values are still plaintext, and reading them fails with an "unsupported format" error. [`GetKeyUsageAsync`](key-management#key-usage) shows how many there are, listing them with an empty root key id.

Add the built-in plaintext decryptor while you migrate. The app can read the old values right away, and `ReEncryptAsync` encrypts them in place:

```csharp
builder.Services.AddEncryption(x => x.UseKey(key).UseLegacyDecryptor(LegacyDecryptor.Plaintext));
```

[Migrating existing data](migrating) walks through it. It also covers columns that are already encrypted by your previous code.
