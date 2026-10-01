using EntityFrameworkCore.Encrypted;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Samples.Encryption.Aes.Common;
using EntityFrameworkCore.Samples.Encryption.Aes.Database;
using EntityFrameworkCore.Samples.Encryption.Aes.Legacy;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
// registered before AddEncryption: the database is migrated before the keys are loaded
builder.Services.AddHostedService<MigrationHostedService>();
builder.Services.AddHostedService<LegacyContractsSeeder>();

// static AES-256 key from configuration; add a key with a higher id to rotate: UseKey(newKey, id: 2).
// The legacy decryptor reads contracts encrypted by the previous code until they're migrated (POST /keys/re-encrypt)
builder.Services
    .AddEncryption(x => x
        .UseKey(builder.Configuration["Encryption:Key"]!)
        .UseLegacyDecryptor<Aes256CbcLegacyDecryptor>())
    .AddDbContext<EncryptedDbContext>(x => x
        .UseNpgsql(builder.Configuration["Database:ConnectionString"])
        .UseEncryption());

builder.Services.AddHealthChecks().AddEncryptionKeys();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapHealthChecks("/health");

app.MapPost("/customers", async (CreateCustomer request, EncryptedDbContext context, CancellationToken ct) =>
{
    var customer = new Customer
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        Email = request.Email,
        Phone = request.Phone,
        Status = CustomerStatus.Active,
        BirthDate = request.BirthDate,
        Address = new Address { Street = request.Street, City = request.City },
        Passport = request.PassportScanBase64 == null ? null : Convert.FromBase64String(request.PassportScanBase64)
    };

    context.Add(customer);
    await context.SaveChangesAsync(ct);
    return Results.Created($"/customers/{customer.Id}", customer);
});

// values are decrypted when entities are loaded
app.MapGet("/customers/{id:guid}", async (Guid id, EncryptedDbContext context, CancellationToken ct)
    => await context.Customers.FindAsync([id], ct) is { } customer ? Results.Ok(customer) : Results.NotFound());

// filtering and sorting work on columns that aren't encrypted
app.MapGet("/customers", (string? name, EncryptedDbContext context, CancellationToken ct)
    => context.Customers
        .Where(x => name == null || x.Name.StartsWith(name))
        .OrderBy(x => x.Name)
        .ToListAsync(ct));

// the email has a blind index: equality queries compare its keyed hash, so they work and use a database index
app.MapGet("/customers/by-email", async (string email, EncryptedDbContext context, CancellationToken ct)
    => await context.Customers.FirstOrDefaultAsync(x => x.Email == email, ct) is { } customer
        ? Results.Ok(customer)
        : Results.NotFound());

// tracked update: changed values are encrypted again on save, and the blind index of the email follows the new value
app.MapPut("/customers/{id:guid}", async (Guid id, UpdateCustomer request, EncryptedDbContext context, CancellationToken ct) =>
{
    if (await context.Customers.FindAsync([id], ct) is not { } customer)
        return Results.NotFound();

    customer.Name = request.Name;
    customer.Email = request.Email;
    customer.Phone = request.Phone;
    customer.Address.Street = request.Street;
    customer.Address.City = request.City;

    await context.SaveChangesAsync(ct);
    return Results.Ok(customer);
});

// bulk update without loading the entity: the new value is encrypted, and its blind index is set in the same statement
app.MapPatch("/customers/{id:guid}/email", async (Guid id, string email, EncryptedDbContext context, CancellationToken ct)
    => await context.Customers
        .Where(x => x.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, email), ct) == 1
        ? Results.NoContent()
        : Results.NotFound());

// encrypted values can be set in bulk too, also for properties without a blind index
app.MapPost("/customers/{id:guid}/block", async (Guid id, EncryptedDbContext context, CancellationToken ct)
    => await context.Customers
        .Where(x => x.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CustomerStatus.Blocked), ct) == 1
        ? Results.NoContent()
        : Results.NotFound());

app.MapDelete("/customers/{id:guid}", async (Guid id, EncryptedDbContext context, CancellationToken ct)
    => await context.Customers.Where(x => x.Id == id).ExecuteDeleteAsync(ct) == 1 ? Results.NoContent() : Results.NotFound());

// what's stored in the database: ciphertext, and the keyed hash of the email
app.MapGet("/customers/stored", (EncryptedDbContext context, CancellationToken ct)
    => context.Database
        .SqlQuery<StoredCustomer>($"""
            SELECT "Name", "Email", "Email_Index" AS "EmailIndex", "Phone", "Status", "BirthDate", "Address_Street" AS "Street", "Passport"
            FROM "Customers" ORDER BY "Name"
            """)
        .ToListAsync(ct));

// the phone has no blind index: comparing it in a query throws instead of silently returning nothing
app.MapGet("/customers/by-phone", async (string phone, EncryptedDbContext context, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await context.Customers.Where(x => x.Phone == phone).ToListAsync(ct));
    }
    catch (EntityFrameworkEncryptionException ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

// migrating from the previous encryption: contracts were seeded with IBANs encrypted with plain AES-256

// old and new values read the same way: the legacy decryptor handles values that aren't in the library's format
app.MapGet("/contracts", (EncryptedDbContext context, CancellationToken ct)
    => context.Contracts.OrderBy(x => x.Number).ToListAsync(ct));

// new contracts are written in the library's format
app.MapPost("/contracts", async (string number, string iban, EncryptedDbContext context, CancellationToken ct) =>
{
    var contract = new Contract { Id = Guid.NewGuid(), Number = number, Iban = iban };
    context.Add(contract);
    await context.SaveChangesAsync(ct);
    return Results.Created($"/contracts/{contract.Id}", contract);
});

// what's stored: the previous code's Base64 AES-CBC values, or the library's format
app.MapGet("/contracts/stored", (EncryptedDbContext context, CancellationToken ct)
    => context.Database
        .SqlQuery<StoredContract>($"""SELECT "Number", "Iban" FROM "Contracts" ORDER BY "Number" """)
        .ToListAsync(ct));

// values per column and key: legacy values have no root key; when none are left, remove UseLegacyDecryptor
app.MapGet("/keys/usage", (IServiceProvider services, CancellationToken ct)
    => services.GetKeyUsageAsync<EncryptedDbContext>(ct));

// migrates legacy values (and values of older keys) to the library's format, in batches, while the app runs
app.MapPost("/keys/re-encrypt", (IServiceProvider services, CancellationToken ct)
    => services.ReEncryptAsync<EncryptedDbContext>(cancellationToken: ct));

app.Run();

internal sealed record CreateCustomer(
    string Name,
    string Email,
    string? Phone,
    DateOnly BirthDate,
    string Street,
    string City,
    string? PassportScanBase64);

internal sealed record UpdateCustomer(string Name, string Email, string? Phone, string Street, string City);

internal sealed record StoredCustomer(
    string Name,
    string Email,
    byte[]? EmailIndex,
    string? Phone,
    string Status,
    string BirthDate,
    string Street,
    byte[]? Passport);

internal sealed record StoredContract(string Number, string Iban);
