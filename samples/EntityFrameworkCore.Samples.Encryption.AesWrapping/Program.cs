using Amazon.KeyManagementService;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping;
using EntityFrameworkCore.Samples.Encryption.AesWrapping.Common;
using EntityFrameworkCore.Samples.Encryption.AesWrapping.Database;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHostedService<MigrationHostedService>();

builder.Services.AddSingleton<IAmazonKeyManagementService>(new AmazonKeyManagementServiceClient(
    new AmazonKeyManagementServiceConfig
    {
        ServiceURL = "http://localhost:4566"
    }));

// two identical configuration approaches
// u can configure wrapping explicitly (1 approach)
// where u can specify connection string for database where data key metadata will be saved
// or it could be configure automatically (2 approach) and registered db context connection string will be used
if (true)
{
    // 1 approach
    builder.Services.AddAwsAesDataKeyWrapping(
        builder.Configuration.GetValue<string>("Database:ConnectionString")!,
        x => x.WithKeyArn(builder.Configuration.GetValue<string>("Database:WrappingKeyId")!));
    
    builder.Services.AddDbContext<EncryptedDbContext>(
        x => x
            .UseNpgsql(builder.Configuration.GetValue<string>("Database:ConnectionString"))
            .UseAes256Encryption());
}
else
#pragma warning disable CS0162 // Unreachable code detected
{
    // 2 approach
    builder.Services.AddDbContext<EncryptedDbContext>(
        x => x
            .UseNpgsql(builder.Configuration.GetValue<string>("Database:ConnectionString"))
            .UseAes256Encryption(),
        x=> x.WithKeyArn(builder.Configuration.GetValue<string>("Database:WrappingKeyId")!));
}
#pragma warning restore CS0162 // Unreachable code detected


var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();

app.MapGet("/passwords", (EncryptedDbContext context, CancellationToken ct)
        => context.EncryptedWrappedPasswords.ToListAsync(ct))
    .WithOpenApi();

app.MapPost("/passwords", async (string password, EncryptedDbContext context, CancellationToken ct) =>
    {
        context.Add(new PasswordWithEncryptionWrapping
        {
            EncryptedFluent = password,
            EncryptedAttribute = password,
            Original = password
        });
        
        await context.SaveChangesAsync(ct);
    })
    .WithOpenApi();

app.Run();