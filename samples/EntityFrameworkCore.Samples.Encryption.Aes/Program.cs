using EntityFrameworkCore.Encrypted;
using EntityFrameworkCore.Samples.Encryption.Aes.Common;
using EntityFrameworkCore.Samples.Encryption.Aes.Database;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHostedService<MigrationHostedService>();

// configure db context with encryption
builder.Services.AddDbContext<EncryptedDbContext>(x => x
    .UseNpgsql(builder.Configuration.GetValue<string>("Database:ConnectionString"))
    .UseAes256Encryption(builder.Configuration.GetValue<string>("Database:TestAesKey")!));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/passwords", (EncryptedDbContext context, CancellationToken ct)
        => context.EncryptedPasswords.ToListAsync(ct))
    .WithOpenApi();

app.MapPost("/passwords", async (string password, EncryptedDbContext context, CancellationToken ct) =>
    {
        context.Add(new PasswordWithEncryption
        {
            EncryptedFluent = password,
            EncryptedAttribute = password,
            Original = password
        });
        
        await context.SaveChangesAsync(ct);
    })
    .WithOpenApi();

app.Run();