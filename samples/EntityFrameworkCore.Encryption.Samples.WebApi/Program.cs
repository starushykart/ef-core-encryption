using Amazon.KeyManagementService;
using EntityFrameworkCore.Encryption;
using EntityFrameworkCore.Encryption.AwsWrapping;
using EntityFrameworkCore.Encryption.Postgres;
using EntityFrameworkCore.Encryption.Samples.WebApi.Database;
using EntityFrameworkCore.Encryption.Samples.WebApi.Models;
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

builder.Services.AddDbContext<ApplicationDbContext>(
    x => x
        .UseNpgsql(builder.Configuration.GetValue<string>("Database:ConnectionString")),
    opt =>
        opt.UseCurrentDatabaseStorage()
            .UseAesWithAwsWrapping(o =>
            {
                o.WrappingKeyArn = builder.Configuration.GetValue<string>("Database:WrappingKeyId")!;
                o.GenerateDataKeyIfNotExist = true;
                o.DataKeyCacheExpiration = builder.Configuration.GetValue<TimeSpan>("Database:DataKeyCacheExpiration");
            }));

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