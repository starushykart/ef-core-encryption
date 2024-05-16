using EntityFrameworkCore.Encrypted.IntegrationTests.Common;
using EntityFrameworkCore.Encrypted.IntegrationTests.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EntityFrameworkCore.Encrypted.IntegrationTests;

public sealed class DatabaseEncryptionTests(PostgresContainerFixture postgres) : BaseDatabaseTest(postgres)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_encrypt_and_decrypt_successfully(bool useFactory)
    {
        await using var scope = Provider.CreateAsyncScope();

        await using var context = useFactory
            ? await Provider.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContextAsync()
            : scope.ServiceProvider.GetRequiredService<TestDbContext>();
        
        var passwordToAdd = Fakers.PasswordFaker.Generate();

        context.Add(passwordToAdd);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var addedPassword = await context.Passwords
            .SingleOrDefaultAsync(x => x.Id == passwordToAdd.Id);

        addedPassword.Should().NotBeNull();
        addedPassword.Should().BeEquivalentTo(passwordToAdd);

        addedPassword!.Original.Should().Be(addedPassword.EncryptedAttribute)
            .And.Be(addedPassword.EncryptedFluent);
    }
    
    [Fact]
    public async Task Should_save_encrypted_data_in_database()
    {
        await using var scope = Provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        
        var passwordToAdd = Fakers.PasswordFaker.Generate();

        context.Add(passwordToAdd);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // directly get field from database to be sure that it was actually encrypted
        await using var connection = new NpgsqlConnection(ConnectionString);

        var cmd = new NpgsqlCommand(
            $"""select "EncryptedFluent" from "Passwords" where "Id"='{passwordToAdd.Id}'""", connection);

        await connection.OpenAsync();
        var passwordDirectFromDb = (await cmd.ExecuteScalarAsync())?.ToString();
        await connection.CloseAsync();

        passwordDirectFromDb.Should().NotBeEmpty();
        passwordDirectFromDb.Should()
            .NotBe(passwordToAdd.Original)
            .And.NotBe(passwordToAdd.EncryptedFluent);
    }
    
    protected override void Configure(IServiceCollection services)
    {
        var key = TestUtils.GenerateAesKeyBase64();

        services
            .AddDbContextFactory<TestDbContext>(x => x
                .UseNpgsql(ConnectionString)
                .UseAes256Encryption(key))
            .AddDbContext<TestDbContext>(x => x
                .UseNpgsql(ConnectionString)
                .UseAes256Encryption(key));
    }

    protected override async Task MigrateAsync(IServiceScope scope)
    {
        await using var dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await dbContext.Database.MigrateAsync();
    }
}