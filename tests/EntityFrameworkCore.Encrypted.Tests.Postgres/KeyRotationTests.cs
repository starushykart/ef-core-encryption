using System.Buffers.Binary;
using System.Security.Cryptography;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

public class KeyRotationTests
{
    private readonly InMemoryKeyWrapper _wrapper = new();
    private readonly InMemoryRootKeyStore _store = new();

    [Fact]
    public void Should_use_fixed_data_key_version()
    {
        using var provider = DocumentDbContext.BuildProvider(x => x.UseKey(RandomNumberGenerator.GetBytes(32)).UseDataKeyVersion(7));

        DataKeyVersion(Encrypt(DocumentDbContext.GetConverter(provider, nameof(Document.Blob)), [1])).Should().Be(7);
    }

    [Fact]
    public void Should_rotate_static_root_key_by_adding_a_key_with_higher_id()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        using var before = DocumentDbContext.BuildProvider(x => x.UseKey(oldKey));
        using var after = DocumentDbContext.BuildProvider(x => x.UseKey(oldKey).UseKey(RandomNumberGenerator.GetBytes(32), id: 2));

        var old = Encrypt(DocumentDbContext.GetConverter(before, nameof(Document.Blob)), [1]);
        var converter = DocumentDbContext.GetConverter(after, nameof(Document.Blob));

        RootKeyId(Encrypt(converter, [2])).Should().Be(2);
        Decrypt(converter, old).Should().Equal(1);
    }

    [Fact]
    public void Should_encrypt_with_the_active_static_key_while_a_new_one_rolls_out()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);
        using var rollingOut = DocumentDbContext.BuildProvider(x => x.UseKey(oldKey).UseKey(newKey, id: 2).UseActiveKey(1));
        using var rolledOut = DocumentDbContext.BuildProvider(x => x.UseKey(oldKey).UseKey(newKey, id: 2));

        var value = Encrypt(DocumentDbContext.GetConverter(rollingOut, nameof(Document.Blob)), [1]);

        RootKeyId(value).Should().Be(1);
        RootKeyId(Encrypt(DocumentDbContext.GetConverter(rolledOut, nameof(Document.Blob)), [2])).Should().Be(2);
        Decrypt(DocumentDbContext.GetConverter(rollingOut, nameof(Document.Blob)), Encrypt(DocumentDbContext.GetConverter(rolledOut, nameof(Document.Blob)), [3]))
            .Should().Equal(3);
    }

    [Fact]
    public void Should_reject_an_active_static_key_that_is_not_configured()
    {
        var act = () => DocumentDbContext.BuildProvider(x => x.UseKey(RandomNumberGenerator.GetBytes(32)).UseActiveKey(2));

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("UseActiveKey(2)*");
    }

    [Fact]
    public void Should_throw_when_root_key_of_value_does_not_exist()
    {
        using var withTwoKeys = DocumentDbContext.BuildProvider(x => x.UseKey(RandomNumberGenerator.GetBytes(32)).UseKey(RandomNumberGenerator.GetBytes(32), id: 2));
        using var withOneKey = DocumentDbContext.BuildProvider(x => x.UseKey(RandomNumberGenerator.GetBytes(32)));

        var value = Encrypt(DocumentDbContext.GetConverter(withTwoKeys, nameof(Document.Blob)), [1]);
        var act = () => Decrypt(DocumentDbContext.GetConverter(withOneKey, nameof(Document.Blob)), value);

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("Root key 2 * not found");
    }

    [Fact]
    public async Task Should_not_rotate_static_root_keys_at_runtime()
    {
        await using var provider = DocumentDbContext.BuildProvider(x => x.UseKey(RandomNumberGenerator.GetBytes(32)));

        var act = () => provider.RotateRootKeyAsync<DocumentDbContext>();

        await act.Should().ThrowAsync<EntityFrameworkEncryptionException>().WithMessage("*higher id*");
    }

    [Fact]
    public async Task Should_generate_root_key_on_first_start_and_unwrap_it_on_next_starts()
    {
        await using var first = BuildWrapped();
        await first.InitializeEncryptionAsync();
        var value = Encrypt(DocumentDbContext.GetConverter(first, nameof(Document.Blob)), [1]);

        _wrapper.GenerateCalls.Should().Be(1);
        _wrapper.UnwrapCalls.Should().Be(0, "the generated plaintext key is used directly");

        await using var second = BuildWrapped();
        await second.InitializeEncryptionAsync();

        Decrypt(DocumentDbContext.GetConverter(second, nameof(Document.Blob)), value).Should().Equal(1);
        _wrapper.GenerateCalls.Should().Be(1);
        _wrapper.UnwrapCalls.Should().Be(1);
        _store.Keys(typeof(DocumentDbContext)).Should().ContainSingle();
    }

    [Fact]
    public async Task Should_unwrap_only_active_root_key_on_start()
    {
        await using (var setup = BuildWrapped())
        {
            await setup.RotateRootKeyAsync<DocumentDbContext>();
            await setup.RotateRootKeyAsync<DocumentDbContext>();
            await setup.RotateRootKeyAsync<DocumentDbContext>();
        }

        await using var provider = BuildWrapped();
        var unwrapsBefore = _wrapper.UnwrapCalls;

        await provider.InitializeEncryptionAsync();

        (_wrapper.UnwrapCalls - unwrapsBefore).Should().Be(1);
        RootKeyId(Encrypt(DocumentDbContext.GetConverter(provider, nameof(Document.Blob)), [1])).Should().Be(3);
    }

    [Fact]
    public async Task Should_load_older_root_key_on_demand()
    {
        await using var provider = BuildWrapped();
        var converter = DocumentDbContext.GetConverter(provider, nameof(Document.Blob));
        var old = Encrypt(converter, [1]);

        await provider.RotateRootKeyAsync<DocumentDbContext>();

        await using var restarted = BuildWrapped();
        await restarted.InitializeEncryptionAsync();
        var unwrapsBefore = _wrapper.UnwrapCalls;

        Decrypt(DocumentDbContext.GetConverter(restarted, nameof(Document.Blob)), old).Should().Equal(1);
        Decrypt(DocumentDbContext.GetConverter(restarted, nameof(Document.Blob)), old).Should().Equal(1);
        (_wrapper.UnwrapCalls - unwrapsBefore).Should().Be(1, "loaded root keys are cached");
    }

    [Fact]
    public async Task Should_load_root_key_once_for_concurrent_reads()
    {
        await using var instanceA = BuildWrapped();
        await using var instanceB = BuildWrapped();
        await instanceB.InitializeEncryptionAsync();

        await instanceA.RotateRootKeyAsync<DocumentDbContext>();
        var writtenByA = Encrypt(DocumentDbContext.GetConverter(instanceA, nameof(Document.Blob)), [1]);

        _wrapper.UnwrapDelay = TimeSpan.FromMilliseconds(200);
        var unwrapsBefore = _wrapper.UnwrapCalls;
        var converterB = DocumentDbContext.GetConverter(instanceB, nameof(Document.Blob));

        var reads = Enumerable.Range(0, 20).Select(_ => Task.Run(() => Decrypt(converterB, writtenByA)));
        var results = await Task.WhenAll(reads);

        results.Should().AllSatisfy(x => x.Should().Equal(1));
        (_wrapper.UnwrapCalls - unwrapsBefore).Should().Be(1, "concurrent reads share one load of the new root key");
    }

    [Fact]
    public void Should_not_zero_keys_shared_with_other_applications()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var original = (byte[])key.Clone();
        var services = new ServiceCollection()
            .AddEncryption(x => x.UseKey(key))
            .AddDbContext<DocumentDbContext>(x => x.UseNpgsql(DocumentDbContext.ConnectionString).UseEncryption());

        var first = services.BuildServiceProvider();
        var value = Encrypt(DocumentDbContext.GetConverter(first, nameof(Document.Blob)), [1]);
        first.Dispose();

        using var second = services.BuildServiceProvider();

        key.Should().Equal(original, "the caller's key must not be zeroed");
        Decrypt(DocumentDbContext.GetConverter(second, nameof(Document.Blob)), value).Should().Equal(1);
    }

    [Fact]
    public async Task Should_pick_up_root_key_rotated_by_another_instance()
    {
        await using var instanceA = BuildWrapped();
        await using var instanceB = BuildWrapped();
        await instanceA.InitializeEncryptionAsync();
        await instanceB.InitializeEncryptionAsync();
        var converterA = DocumentDbContext.GetConverter(instanceA, nameof(Document.Blob));
        var converterB = DocumentDbContext.GetConverter(instanceB, nameof(Document.Blob));

        var newRootKeyId = await instanceA.RotateRootKeyAsync<DocumentDbContext>();
        var writtenByA = Encrypt(converterA, [1]);

        RootKeyId(writtenByA).Should().Be(newRootKeyId);
        Decrypt(converterB, writtenByA).Should().Equal(1);
        RootKeyId(Encrypt(converterB, [2])).Should().Be(newRootKeyId, "B switches to the newer root key it has seen");
    }

    [Fact]
    public async Task Should_switch_to_root_key_rotated_by_another_instance_on_refresh()
    {
        await using var instanceA = BuildWrapped();
        await using var writeOnly = BuildWrapped();
        await instanceA.InitializeEncryptionAsync();
        await writeOnly.InitializeEncryptionAsync();
        var converter = DocumentDbContext.GetConverter(writeOnly, nameof(Document.Blob));

        var newRootKeyId = await instanceA.RotateRootKeyAsync<DocumentDbContext>();
        RootKeyId(Encrypt(converter, [1])).Should().Be(1, "nothing encrypted with the new root key was read yet");

        var unwrapsBefore = _wrapper.UnwrapCalls;
        await writeOnly.GetRequiredService<DataKeyRing>().RefreshAsync(CancellationToken.None);
        await writeOnly.GetRequiredService<DataKeyRing>().RefreshAsync(CancellationToken.None);

        RootKeyId(Encrypt(converter, [1])).Should().Be(newRootKeyId);
        (_wrapper.UnwrapCalls - unwrapsBefore).Should().Be(1, "the new root key is loaded once, checks without changes only read the store");
    }

    [Fact]
    public async Task Should_refresh_root_keys_periodically_when_configured()
    {
        await using var instanceA = BuildWrapped();
        await using var writeOnly = BuildWrapped(x => x.RefreshRootKeysEvery(TimeSpan.FromMilliseconds(50)));
        await instanceA.InitializeEncryptionAsync();
        await writeOnly.InitializeEncryptionAsync();

        var hostedServices = writeOnly.GetServices<IHostedService>().ToList();
        foreach (var service in hostedServices)
            await service.StartAsync(CancellationToken.None);

        var newRootKeyId = await instanceA.RotateRootKeyAsync<DocumentDbContext>();
        var converter = DocumentDbContext.GetConverter(writeOnly, nameof(Document.Blob));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (RootKeyId(Encrypt(converter, [1])) != newRootKeyId && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        RootKeyId(Encrypt(converter, [1])).Should().Be(newRootKeyId);

        foreach (var service in hostedServices)
            await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Should_keep_active_root_key_when_refresh_fails()
    {
        await using var provider = BuildWrapped();
        await provider.InitializeEncryptionAsync();
        _store.Unavailable = true;

        await provider.GetRequiredService<DataKeyRing>().RefreshAsync(CancellationToken.None);

        _store.Unavailable = false;
        var converter = DocumentDbContext.GetConverter(provider, nameof(Document.Blob));
        RootKeyId(Encrypt(converter, [1])).Should().Be(1);
    }

    [Fact]
    public async Task Should_create_root_key_once_when_instances_start_concurrently()
    {
        var providers = Enumerable.Range(0, 10).Select(_ => BuildWrapped()).ToList();

        await Task.WhenAll(providers.Select(x => x.InitializeEncryptionAsync()));

        _store.Keys(typeof(DocumentDbContext)).Should().ContainSingle();
        var value = Encrypt(DocumentDbContext.GetConverter(providers[0], nameof(Document.Blob)), [1]);
        providers.Should().AllSatisfy(x => Decrypt(DocumentDbContext.GetConverter(x, nameof(Document.Blob)), value).Should().Equal(1));

        providers.ForEach(x => x.Dispose());
    }

    [Fact]
    public async Task Should_throw_when_root_key_is_missing_and_creation_is_disabled()
    {
        await using var provider = BuildWrapped(x => x.CreateRootKeyIfMissing(false));

        var act = () => provider.InitializeEncryptionAsync();

        await act.Should().ThrowAsync<EntityFrameworkEncryptionException>().WithMessage("*creating it is disabled*");
        _wrapper.GenerateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Should_defer_key_loading_when_store_is_not_ready_on_start()
    {
        _store.Unavailable = true;
        await using var provider = BuildWrapped();

        await provider.InitializeEncryptionAsync();

        _store.Unavailable = false;
        var converter = DocumentDbContext.GetConverter(provider, nameof(Document.Blob));
        Decrypt(converter, Encrypt(converter, [1])).Should().Equal(1);
    }

    private ServiceProvider BuildWrapped(Action<EncryptionBuilder>? configure = null)
        => DocumentDbContext.BuildProvider(x =>
        {
            x.UseKeyWrapper(_ => _wrapper).UseRootKeyStore(_ => _store);
            configure?.Invoke(x);
        });

    private static byte[] Encrypt(Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter converter, byte[] value)
        => (byte[])converter.ConvertToProvider(value)!;

    private static byte[] Decrypt(Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter converter, byte[] value)
        => (byte[])converter.ConvertFromProvider(value)!;

    private static int RootKeyId(byte[] encrypted)
        => BinaryPrimitives.ReadUInt16BigEndian(encrypted.AsSpan(1, 2));

    private static uint DataKeyVersion(byte[] encrypted)
        => BinaryPrimitives.ReadUInt32BigEndian(encrypted.AsSpan(3, 4));
}
