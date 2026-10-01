using EntityFrameworkCore.Encrypted.Common.Keys;
using System.Security.Cryptography;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Keys;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

// asserts on EF model caching; EF shares one model cache between all applications in the process, so other tests running in parallel can evict models
[Collection(nameof(EncryptionServicesTests))]
public class EncryptionServicesTests
{
    private const string ConnectionString = "Host=localhost";

    [Fact]
    public void Should_reuse_model_across_scopes()
    {
        using var provider = BuildProvider(new CountingRootKeyProvider());

        var models = Enumerable.Range(0, 30)
            .Select(_ =>
            {
                using var scope = provider.CreateScope();
                return scope.ServiceProvider.GetRequiredService<UnitDbContext>().Model;
            })
            .Distinct()
            .ToList();

        models.Should().ContainSingle("EF must reuse one internal service provider and model per application");
    }

    [Fact]
    public void Should_isolate_models_between_service_providers()
    {
        using var first = BuildProvider(new CountingRootKeyProvider());
        using var second = BuildProvider(new CountingRootKeyProvider());

        var firstModel = first.CreateScope().ServiceProvider.GetRequiredService<UnitDbContext>().Model;
        var secondModel = second.CreateScope().ServiceProvider.GetRequiredService<UnitDbContext>().Model;

        firstModel.Should().NotBeSameAs(secondModel, "models capture the key ring of their application");
    }

    [Fact]
    public void Should_support_many_applications_in_one_process()
    {
        // EF throws ManyServiceProvidersCreatedWarning after 20 internal service providers,
        // e.g. integration tests creating an application (WebApplicationFactory) per test
        var models = Enumerable.Range(0, 40)
            .Select(_ =>
            {
                using var provider = BuildProvider(new CountingRootKeyProvider());
                using var scope = provider.CreateScope();
                return scope.ServiceProvider.GetRequiredService<UnitDbContext>().Model;
            })
            .Distinct()
            .ToList();

        models.Should().HaveCount(40);
    }

    [Fact]
    public void Should_evict_models_of_disposed_applications()
    {
        // EF model cache fits ~40 models; without eviction later applications would rebuild the model per scope
        for (var i = 0; i < 60; i++)
        {
            using var disposed = BuildProvider(new CountingRootKeyProvider());
            _ = disposed.CreateScope().ServiceProvider.GetRequiredService<UnitDbContext>().Model;
        }

        Should_reuse_model_across_scopes();
    }

    [Fact]
    public void Should_keep_custom_model_cache_key_factory()
    {
        using var provider = new ServiceCollection()
            .AddEncryption(x => x.UseRootKeyProvider(_ => new CountingRootKeyProvider()))
            .AddDbContext<UnitDbContext>(x => x
                .UseNpgsql(ConnectionString)
                .UseEncryption()
                .ReplaceService<IModelCacheKeyFactory, CountingModelCacheKeyFactory>())
            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        _ = scope.ServiceProvider.GetRequiredService<UnitDbContext>().Model;

        CountingModelCacheKeyFactory.Calls.Should().BePositive();
    }

    [Fact]
    public void Should_load_key_lazily_on_first_use()
    {
        var source = new CountingRootKeyProvider();
        using var provider = BuildProvider(source);
        using var scope = provider.CreateScope();

        var converter = GetConverter(scope.ServiceProvider.GetRequiredService<UnitDbContext>());
        source.Calls.Should().Be(0, "building the model must not require keys");

        var encrypted = converter.ConvertToProvider("secret");
        var decrypted = converter.ConvertFromProvider(encrypted);

        encrypted.Should().NotBe("secret");
        decrypted.Should().Be("secret");
        source.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Should_load_keys_eagerly_with_InitializeEncryptionAsync()
    {
        var source = new CountingRootKeyProvider();
        await using var provider = BuildProvider(source);

        await provider.InitializeEncryptionAsync();
        await provider.InitializeEncryptionAsync();

        using var scope = provider.CreateScope();
        GetConverter(scope.ServiceProvider.GetRequiredService<UnitDbContext>()).ConvertToProvider("secret");

        source.Calls.Should().Be(1);
        source.Contexts.Should().Equal(typeof(UnitDbContext));
    }

    [Fact]
    public async Task Should_load_keys_before_hosted_services_start()
    {
        var source = new CountingRootKeyProvider();

        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddEncryption(x => x.UseRootKeyProvider(_ => source))
            .AddDbContext<UnitDbContext>(x => x.UseNpgsql(ConnectionString).UseEncryption())
            .AddSingleton(source)
            .AddSingleton<KeyUsageRecordingService>()
            .AddHostedService(sp => sp.GetRequiredService<KeyUsageRecordingService>());

        using var host = builder.Build();
        await host.StartAsync();

        host.Services.GetRequiredService<KeyUsageRecordingService>().SourceCallsOnStart.Should().Be(1);
        await host.StopAsync();
    }

    [Fact]
    public async Task Should_retry_key_loading_after_failure()
    {
        var source = new CountingRootKeyProvider { FailFirstCall = true };
        await using var provider = BuildProvider(source);

        var first = () => provider.InitializeEncryptionAsync();
        await first.Should().ThrowAsync<InvalidOperationException>();

        await provider.InitializeEncryptionAsync();
        source.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Should_reject_root_key_of_invalid_size()
    {
        await using var provider = BuildProvider(new CountingRootKeyProvider { KeySize = 16 });

        var act = () => provider.InitializeEncryptionAsync();

        await act.Should().ThrowAsync<EntityFrameworkEncryptionException>().WithMessage("*32 bytes*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(24)]
    public void UseKey_should_reject_non_256_bit_keys(int size)
    {
        var act = () => new ServiceCollection().AddEncryption(x => x.UseKey(new byte[size]));

        act.Should().Throw<EntityFrameworkEncryptionException>();
    }

    [Fact]
    public async Task AddEncryption_should_keep_first_registration()
    {
        var first = new CountingRootKeyProvider();
        var second = new CountingRootKeyProvider();

        await using var provider = new ServiceCollection()
            .AddEncryption(x => x.UseRootKeyProvider(_ => first).UseDataKeyVersion(1))
            .AddEncryption(x => x.UseRootKeyProvider(_ => second).UseDataKeyVersion(2))
            .AddDbContext<UnitDbContext>(x => x.UseNpgsql(ConnectionString).UseEncryption())
            .BuildServiceProvider();

        await provider.InitializeEncryptionAsync();

        first.Calls.Should().Be(1);
        second.Calls.Should().Be(0);
        provider.GetRequiredService<DataKeyRing>().GetEncryptionKey(typeof(UnitDbContext)).KeyId.DataKeyVersion.Should().Be(1);
        provider.GetServices<IHostedService>().Should().HaveCount(2, "hosted services are registered once");
    }

    [Fact]
    public void AddEncryption_should_require_key_source()
    {
        var act = () => new ServiceCollection().AddEncryption(_ => { });

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*UseKey*");
    }

    [Fact]
    public void UseEncryption_should_throw_when_services_are_not_registered()
    {
        using var provider = new ServiceCollection()
            .AddDbContext<UnitDbContext>(x => x.UseNpgsql(ConnectionString).UseEncryption())
            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<UnitDbContext>();

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*AddEncryption*");
    }

    [Fact]
    public void Context_created_outside_of_DI_should_build_model_but_throw_on_use()
    {
        var options = new DbContextOptionsBuilder<UnitDbContext>()
            .UseNpgsql(ConnectionString)
            .UseEncryption()
            .Options;
        using var context = new UnitDbContext(options);

        var converter = GetConverter(context);
        var act = () => converter.ConvertToProvider("secret");

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("*not configured*");
    }

    private static ServiceProvider BuildProvider(IRootKeyProvider source)
        => new ServiceCollection()
            .AddEncryption(x => x.UseRootKeyProvider(_ => source))
            .AddDbContext<UnitDbContext>(x => x.UseNpgsql(ConnectionString).UseEncryption())
            .BuildServiceProvider();

    private static ValueConverter GetConverter(DbContext context)
        => context.Model
            .FindEntityType(typeof(UnitEntity))!
            .FindProperty(nameof(UnitEntity.Secret))!
            .GetValueConverter()!;

    private sealed class CountingRootKeyProvider : IRootKeyProvider
    {
        private int _calls;

        public int Calls => _calls;
        public List<Type> Contexts { get; } = [];
        public bool FailFirstCall { get; init; }
        public int KeySize { get; init; } = 32;

        public Task<RootKey> GetActiveRootKeyAsync(Type dbContextType, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            lock (Contexts)
                Contexts.Add(dbContextType);

            if (FailFirstCall && call == 1)
                throw new InvalidOperationException("Key source is unavailable");

            return Task.FromResult(new RootKey(1, RandomNumberGenerator.GetBytes(KeySize)));
        }

        public Task<RootKey?> GetRootKeyAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken)
            => Task.FromResult<RootKey?>(null);

        public Task<int?> GetActiveRootKeyIdAsync(Type dbContextType, CancellationToken cancellationToken)
            => Task.FromResult<int?>(1);

        public Task<RootKey> RotateRootKeyAsync(Type dbContextType, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<int> RewrapRootKeysAsync(Type dbContextType, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class CountingModelCacheKeyFactory : IModelCacheKeyFactory
    {
        private static int _calls;

        public static int Calls => _calls;

        public object Create(DbContext context, bool designTime)
        {
            Interlocked.Increment(ref _calls);
            return (context.GetType(), designTime, "custom");
        }
    }

    private sealed class KeyUsageRecordingService(CountingRootKeyProvider source) : IHostedService
    {
        public int SourceCallsOnStart { get; private set; } = -1;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            SourceCallsOnStart = source.Calls;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [CollectionDefinition(nameof(EncryptionServicesTests), DisableParallelization = true)]
    public sealed class NonParallelCollection;

    public sealed class UnitDbContext(DbContextOptions<UnitDbContext> options) : DbContext(options)
    {
        public DbSet<UnitEntity> Entities => Set<UnitEntity>();
    }

    public sealed class UnitEntity
    {
        public Guid Id { get; set; }

        [Encrypted]
        public string? Secret { get; set; }
    }
}
