using Amazon.KeyManagementService;
using EntityFrameworkCore.Encryption.Postgres.AwsWrapping;
using EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Samples.Encryption.Database;
using EntityFrameworkCore.Samples.Encryption.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<MigrationHostedService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IAmazonKeyManagementService>(new AmazonKeyManagementServiceClient(
    new AmazonKeyManagementServiceConfig
    {
        ServiceURL = "http://localhost:4566"
    }));

// first configuration approach
// builder.Services.AddAwsAesDataKeyWrapping(
//     builder.Configuration.GetValue<string>("Database:ConnectionString")!,
//     x => x.WithKeyArn(builder.Configuration.GetValue<string>("Database:WrappingKeyId")));
//
// builder.Services.AddDbContext<ApplicationDbContext>(
//     x => x
//         .UseNpgsql(builder.Configuration.GetValue<string>("Database:ConnectionString"))
//         .UseAes256Encryption());

// second configuration approach
builder.Services.AddDbContext<ApplicationDbContext>(
    x => x
        .UseNpgsql(builder.Configuration.GetValue<string>("Database:ConnectionString"))
        .UseAes256Encryption(),
    x=> x.WithKeyArn(builder.Configuration.GetValue<string>("Database:WrappingKeyId")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/passwords", (ApplicationDbContext context, CancellationToken ct)
        => context.Passwords.ToListAsync(ct))
    .WithOpenApi();

app.MapPost("/passwords", async (string password, ApplicationDbContext context, CancellationToken ct) =>
    {
        context.Add(new Password
        {
            EncryptedFluent = password,
            EncryptedAttribute = password,
            Original = password
        });
        
        await context.SaveChangesAsync(ct);
    })
    .WithOpenApi();

app.Run();