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

If you need to look up rows by an encrypted value, add a [blind index](blind-indexes) to the column. Then `Where(x => x.Email == email)` just works.

For everything else, the library checks your LINQ queries. If a query would compare, search, sort or group by an encrypted column in the database, it throws an `EntityFrameworkEncryptionException` before anything is sent:

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
| aggregates | `Max(x => x.Email)`, `Min(...)`, `Sum(x => x.Salary)`, `Average(...)` |
| joins on encrypted columns | `Join(..., x => x.Email, ...)` |
| filtering or sorting a projection | `Select(x => new { x.Id, x.Email }).Where(a => a.Email == email)`, also DTOs, records and `let` |
| values computed from an encrypted column | `(x.Email ?? "") == email`, `x.Email + "" == email`, `OrderBy(x => x.Email ?? "")`, `x.Salary * 12 > limit`, `Math.Abs(x.Balance)` |
| encrypted bools as conditions | `Where(x => x.IsVip)`, `!x.IsVip`, `Count(x => x.IsVip)`, `x.IsVip ? 1 : 0` |
| subqueries returning an encrypted value | `Where(x => x.Name == db.Users.Select(u => u.Email).First())` |
| aggregates over groups | `GroupBy(x => x.Country, x => x.Email).Select(g => g.Max())` |
| `EF.Property` | `Where(x => EF.Property<string>(x, "Email") == email)` |

Filter before you project, or filter by columns that aren't encrypted. That also covers query builders that put filters on projections, such as OData or GraphQL over AutoMapper's `ProjectTo`.

Global query filters (`HasQueryFilter`) follow the same rules, and are checked by the first query of the context. Only null checks work there: a filter isn't rewritten to use a blind index.

Raw SQL (`FromSql`, `ExecuteSql`) isn't checked, so be careful there.

## Finding a row by an encrypted value

The best way is a [blind index](blind-indexes):

```csharp
e.Property(x => x.Email).IsEncrypted().HasBlindIndex(v => v.Trim().ToLowerInvariant());

var customer = await db.Customers.SingleOrDefaultAsync(x => x.Email == email);
```

Without one, narrow things down with columns that aren't encrypted, and then compare the decrypted values in memory:

```csharp
var candidates = db.Customers.AsNoTracking().Where(x => x.Name.StartsWith(name));

await foreach (var customer in candidates.AsAsyncEnumerable())
{
    if (string.Equals(customer.Email, email, StringComparison.OrdinalIgnoreCase))
        return customer;
}
```

That works well as long as the other filters keep the number of candidates small.

## Bulk updates

`ExecuteUpdate` encrypts the new values, just like `SaveChanges` does:

```csharp
await db.Customers
    .Where(x => x.Id == id)
    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, "new@example.com"));
```

The usual rule still applies to the filter: `ExecuteUpdate` and `ExecuteDelete` can't filter by an encrypted column, unless it has a [blind index](blind-indexes).

Setting columns from other columns runs entirely in the database, which can't encrypt or decrypt. So these throw:

```csharp
s.SetProperty(x => x.Email, x => x.BackupEmail);   // the database can't encrypt the copied value
s.SetProperty(x => x.Notes, x => x.Email);         // would copy ciphertext into a plain column
```

Load the entities and use `SaveChanges` for those.
