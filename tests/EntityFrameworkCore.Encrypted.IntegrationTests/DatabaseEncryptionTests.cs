using EntityFrameworkCore.Encrypted.IntegrationTests.Common;
using EntityFrameworkCore.Encrypted.IntegrationTests.Fixtures;
using EntityFrameworkCore.Encrypted.Tests.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EntityFrameworkCore.Encrypted.IntegrationTests;

public sealed class DatabaseEncryptionTests(PostgresContainerFixture postgres) : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private AsyncServiceScope _testScope;
    
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_encrypt_and_decrypt_successfully(bool useFactory)
    {
        var context = await GetTestDbContextAsync(useFactory);
        
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
    
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_save_encrypted_data_in_database(bool useFactory)
    {
        var context = await GetTestDbContextAsync(useFactory);
        
        var passwordToAdd = Fakers.PasswordFaker.Generate();

        context.Add(passwordToAdd);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // directly get field from database to be sure that it was actually encrypted
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);

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

    private async Task<TestDbContext> GetTestDbContextAsync(bool useFactory)
    {
        if (!useFactory)
            return _testScope.ServiceProvider.GetRequiredService<TestDbContext>();
        
        var factory = _testScope.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    public async Task InitializeAsync()
    {
        var key = TestUtils.GenerateAesKeyBase64();

        var serviceProvider = new ServiceCollection()
            .AddDbContextFactory<TestDbContext>(x => x
                .UseNpgsql(postgres.ConnectionString)
                .UseAes256Encryption(key))
            .AddDbContext<TestDbContext>(x => x
                .UseNpgsql(postgres.ConnectionString)
                .UseAes256Encryption(key))
            .BuildServiceProvider(true);

        _testScope = serviceProvider.CreateAsyncScope();
        
        var dbContext = _testScope.ServiceProvider.GetRequiredService<TestDbContext>();
        await dbContext.Database.MigrateAsync();
        dbContext.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
        => await _testScope.DisposeAsync();
}