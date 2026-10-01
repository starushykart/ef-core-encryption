using EntityFrameworkCore.Encrypted;
using EntityFrameworkCore.Samples.Encryption.Aes.Common;
using EntityFrameworkCore.Samples.Encryption.Aes.Database;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddHostedService<MigrationHostedService>();

// configure db context with encryption
builder.Services
    .AddEncryption(x => x.UseKey(builder.Configuration.GetValue<string>("Database:TestAesKey")!))
    .AddDbContext<EncryptedDbContext>(x => x
        .UseNpgsql(builder.Configuration.GetValue<string>("Database:ConnectionString"))
        .UseEncryption());

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

app.MapGet("/passwords", (EncryptedDbContext context, CancellationToken ct)
        => context.EncryptedPasswords.ToListAsync(ct));

app.MapPost("/passwords", async (string password, EncryptedDbContext context, CancellationToken ct) =>
    {
        context.Add(new PasswordWithEncryption
        {
            EncryptedFluent = password,
            EncryptedAttribute = password,
            Original = password
        });
        
        await context.SaveChangesAsync(ct);
    });

app.Run();