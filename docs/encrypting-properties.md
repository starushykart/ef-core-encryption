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

Your conversion always runs first, and its result is what gets encrypted.

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

## Why there's no maximum length

Putting `[MaxLength]`, `HasMaxLength` or a sized column type like `varchar(100)` on an encrypted property throws when the model is built. That's deliberate. The ciphertext is 35 bytes longer than your value, and strings grow by another third when they're Base64 encoded, so the limit would cut off ciphertext at unexpected points. Validate the length of the value in your application instead.

## Encrypting a column that already has data

Marking an existing column as encrypted doesn't encrypt the rows that are already there. Those values are still plaintext, and reading them fails with an "unsupported format" error. [`GetKeyUsageAsync`](key-management#key-usage) shows how many there are, listing them with an empty root key id.

The safest way to migrate is to add a new encrypted property and copy the values over in application code, in batches: load the rows, assign the new property and call `SaveChanges`. Once every row is copied, drop the old column.
