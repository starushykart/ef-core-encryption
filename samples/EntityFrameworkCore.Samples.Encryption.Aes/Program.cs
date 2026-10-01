using EntityFrameworkCore.Encrypted;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Samples.Encryption.Aes.Common;
using EntityFrameworkCore.Samples.Encryption.Aes.Database;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHostedService<MigrationHostedService>();

// static AES-256 key from configuration; add a key with a higher id to rotate: UseKey(newKey, id: 2)
builder.Services
    .AddEncryption(x => x.UseKey(builder.Configuration["Encryption:Key"]!))
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

// encrypted columns can't be compared in the database: narrow down by other columns, then check the value in memory
app.MapGet("/customers/by-email", async (string email, string? name, EncryptedDbContext context, CancellationToken ct) =>
{
    var candidates = context.Customers.AsNoTracking().Where(x => name == null || x.Name.StartsWith(name));

    await foreach (var customer in candidates.AsAsyncEnumerable().WithCancellation(ct))
    {
        if (string.Equals(customer.Email, email, StringComparison.OrdinalIgnoreCase))
            return Results.Ok(customer);
    }

    return Results.NotFound();
});

// comparing an encrypted column in a query throws instead of silently returning nothing
app.MapGet("/customers/by-email/unsupported", async (string email, EncryptedDbContext context, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await context.Customers.Where(x => x.Email == email).ToListAsync(ct));
    }
    catch (EntityFrameworkEncryptionException ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.Run();

internal sealed record CreateCustomer(
    string Name,
    string Email,
    string? Phone,
    DateOnly BirthDate,
    string Street,
    string City,
    string? PassportScanBase64);
