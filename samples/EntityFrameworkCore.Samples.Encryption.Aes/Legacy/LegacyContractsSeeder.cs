using EntityFrameworkCore.Samples.Encryption.Aes.Database;
using Microsoft.EntityFrameworkCore;

namespace EntityFrameworkCore.Samples.Encryption.Aes.Legacy;

/// <summary>
/// Simulates the database before the migration: contracts written by the previous code, with IBANs encrypted
/// with plain AES-256. Raw SQL, so the library isn't involved.
/// </summary>
public sealed class LegacyContractsSeeder(IServiceScopeFactory scopeFactory, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptedDbContext>();

        if (await context.Contracts.AnyAsync(cancellationToken))
            return;

        var key = Convert.FromBase64String(configuration["Encryption:LegacyKey"]!);

        foreach (var (number, iban) in new[] { ("C-001", "DE89370400440532013000"), ("C-002", "GB29NWBK60161331926819"), ("C-003", "FR1420041010050500013M02606") })
        {
            await context.Database.ExecuteSqlAsync(
                $"""INSERT INTO "Contracts" ("Id", "Number", "Iban") VALUES ({Guid.NewGuid()}, {number}, {Aes256Cbc.Encrypt(iban, key)})""",
                cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
