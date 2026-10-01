---
title: Queries
nav_order: 6
---

# Queries
{: .no_toc }

1. TOC
{:toc}

---

## Why you can't filter by encrypted columns

Each value is encrypted with a fresh random nonce, so encrypting the same email twice gives you two completely different ciphertexts. That's good for security, because nobody can tell that two rows hold the same value. But it also means the database can't compare encrypted values. A `WHERE Email = @email` would compare your parameter to random bytes and quietly return nothing.

To save you from bugs like that, the library checks your LINQ queries. If a query would compare, search, sort or group by an encrypted column in the database, it throws an `EntityFrameworkEncryptionException` before anything is sent:

```
Customer.Email is encrypted and can't be compared, searched, sorted or grouped in a query: the database only sees
random ciphertext, so the result would be wrong. Only '== null' and '!= null' are supported; filter by other columns
and check the value in memory
```

## What works

Pretty much everything except comparing encrypted columns:

```csharp
// filter, sort and group by columns that aren't encrypted
db.Customers.Where(x => x.Name.StartsWith("A")).OrderBy(x => x.Name);

// check encrypted columns for null
db.Customers.Where(x => x.Phone != null);

// load and project encrypted columns, which are decrypted after loading
db.Customers.Where(x => x.Id == id).Select(x => new { x.Name, x.Email });

// paging, as long as you order by something else
db.Customers.OrderBy(x => x.Id).Select(x => x.Email).Skip(20).Take(10);

// your own functions in the final projection
db.Customers.Select(x => Mask(x.Email));
```

## What throws

| Kind of query | Example |
|---------------|---------|
| equality | `Where(x => x.Email == email)`, `x.Email != email` |
| comparing two encrypted columns | `Where(x => x.Email == x.BackupEmail)` |
| `IN` lists | `Where(x => emails.Contains(x.Email))` |
| string functions and `LIKE` | `x.Email.StartsWith(...)`, `x.Email.Length`, `EF.Functions.Like(x.Email, ...)` |
| sorting | `OrderBy(x => x.Email)`, `ThenBy(x => x.Email)` |
| grouping | `GroupBy(x => x.Email)` |
| set operations | `Select(x => x.Email).Distinct()`, `Union`, `Except`, `Intersect` |
| aggregates | `Max(x => x.Email)`, `Min(...)` |
| joins on encrypted columns | `Join(..., x => x.Email, ...)` |
| filtering a projection | `Select(x => x.Email).Where(e => e == email)` |
| `EF.Property` | `Where(x => EF.Property<string>(x, "Email") == email)` |

Raw SQL (`FromSql`, `ExecuteSql`) isn't checked, so be careful there.

## Finding a row by an encrypted value

The simplest approach is to narrow things down with columns that aren't encrypted, and then compare the decrypted values in memory:

```csharp
var candidates = db.Customers.AsNoTracking().Where(x => x.Name.StartsWith(name));

await foreach (var customer in candidates.AsAsyncEnumerable())
{
    if (string.Equals(customer.Email, email, StringComparison.OrdinalIgnoreCase))
        return customer;
}
```

That works well as long as the other filters keep the number of candidates small.

If you need fast lookups by an encrypted value in a big table, store a keyed hash (an HMAC) of the value in a separate indexed column and query by that:

```csharp
public class Customer
{
    [Encrypted]
    public string Email { get; set; } = null!;

    // HMAC-SHA256 of the normalized email: equal emails give equal hashes, but you can't get the email back
    public byte[] EmailHash { get; set; } = null!;
}

var hash = HMACSHA256.HashData(hashKey, Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
var customer = await db.Customers.SingleOrDefaultAsync(x => x.EmailHash == hash);
```

{: .note }
A hash column does reveal which rows share the same value. Use a separate secret key for it (not your encryption key), and only add hash columns for values you really need to look up.

## Bulk updates

`ExecuteUpdate` encrypts the new values, just like `SaveChanges` does:

```csharp
await db.Customers
    .Where(x => x.Id == id)
    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, "new@example.com"));
```

The usual rule still applies to the filter: `ExecuteUpdate` and `ExecuteDelete` can't filter by an encrypted column.
