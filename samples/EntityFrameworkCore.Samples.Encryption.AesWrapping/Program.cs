using Amazon.KeyManagementService;
using EntityFrameworkCore.Encryption.Postgres.AwsWrapping;
using EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Common;
using EntityFrameworkCore.Samples.Encryption.AesWrapping.Database;
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

// two identical configuration approaches
// u can configure wrapping explicitly (1 approach)
// where u can specify connection string for database where data key metadata will be saved
// or it could be configure automatically (2 approach) and registered db context connection string will be used
if (true)
{
    // 1 approach
    builder.Services.AddAwsAesDataKeyWrapping(
        builder.Configuration.GetValue<string>("Database:ConnectionString")!,
        x => x.WithKeyArn(builder.Configuration.GetValue<string>("Database:WrappingKeyId")));
    
    builder.Services.AddDbContext<EncryptedWrappedDbContext>(
        x => x
            .UseNpgsql(builder.Configuration.GetValue<string>("Database:ConnectionString"))
            .UseAes256Encryption());
}
else
{
    // 2 approach
    builder.Services.AddDbContext<EncryptedWrappedDbContext>(
        x => x
            .UseNpgsql(builder.Configuration.GetValue<string>("Database:ConnectionString"))
            .UseAes256Encryption(),
        x=> x.WithKeyArn(builder.Configuration.GetValue<string>("Database:WrappingKeyId")));
}


var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();

app.MapGet("/passwords", (EncryptedWrappedDbContext context, CancellationToken ct)
        => context.EncryptedWrappedPasswords.ToListAsync(ct))
    .WithOpenApi();

app.MapPost("/passwords", async (string password, EncryptedWrappedDbContext context, CancellationToken ct) =>
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