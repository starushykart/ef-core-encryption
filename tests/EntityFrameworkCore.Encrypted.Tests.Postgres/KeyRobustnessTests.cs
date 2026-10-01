using System.Security.Cryptography;
using AwesomeAssertions;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Keys;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

/// <summary>Failures of key management services and stores that must not stop the application or break keys.</summary>
public class KeyRobustnessTests
{
    [Fact]
    public async Task Should_not_throw_when_refresh_times_out()
    {
        // e.g. an HttpClient timeout in a key wrapper: a TaskCanceledException without cancellation being requested
        await using var provider = DocumentDbContext.BuildProvider(x => x.UseRootKeyProvider(_ => new TimingOutProvider()));
        var keyRing = provider.GetRequiredService<DataKeyRing>();
        await keyRing.LoadAsync(typeof(DocumentDbContext), CancellationToken.None);

        var refresh = () => keyRing.RefreshAsync(CancellationToken.None);

        await refresh.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Should_not_create_root_key_ids_that_do_not_fit_into_values()
    {
        var store = new InMemoryRootKeyStore();
        var wrapper = new InMemoryKeyWrapper();
        var last = await wrapper.GenerateAsync(ushort.MaxValue, CancellationToken.None);
        await store.TryAddAsync(typeof(DocumentDbContext),
            new WrappedRootKey(ushort.MaxValue, last.WrappingKeyId, last.WrappedKey, DateTimeOffset.UtcNow), CancellationToken.None);

        await using var provider = DocumentDbContext.BuildProvider(x => x.UseKeyWrapper(_ => wrapper).UseRootKeyStore(_ => store));

        var rotate = () => provider.RotateRootKeyAsync<DocumentDbContext>();

        (await rotate.Should().ThrowAsync<EntityFrameworkEncryptionException>()).WithMessage("*maximum number of root keys*");
        store.Keys(typeof(DocumentDbContext)).Should().ContainSingle();
    }

    [Fact]
    public async Task Should_wrap_blind_index_key_with_a_key_derived_from_the_static_key()
    {
        var staticKey = RandomNumberGenerator.GetBytes(32);
        var store = new InMemoryRootKeyStore();

        await using var provider = DocumentDbContext.BuildProvider(x => x.UseKey(staticKey).UseRootKeyStore(_ => store));
        var keyRing = provider.GetRequiredService<DataKeyRing>();
        await keyRing.LoadIndexKeysAsync(typeof(DocumentDbContext), CancellationToken.None);

        var stored = store.Keys(typeof(DocumentDbContext)).Should().ContainSingle().Which;
        var unwrapWithStaticKey = () => Unwrap(staticKey, stored.WrappedKey);
        var indexKey = Unwrap(HKDF.DeriveKey(HashAlgorithmName.SHA256, staticKey, 32, info: "efenc:wrap:index-key"u8.ToArray()), stored.WrappedKey);

        unwrapWithStaticKey.Should().Throw<AuthenticationTagMismatchException>("the static key only derives keys");
        keyRing.GetIndexKey(typeof(DocumentDbContext), "Documents.Text")
            .Should().Equal(HKDF.DeriveKey(HashAlgorithmName.SHA256, indexKey, 32, info: "efenc:bidx:Documents.Text"u8.ToArray()));
    }

    private static byte[] Unwrap(byte[] key, byte[] wrapped)
    {
        var plaintext = new byte[wrapped.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(wrapped.AsSpan(0, 12), wrapped.AsSpan(12, plaintext.Length), wrapped.AsSpan(12 + plaintext.Length), plaintext, "efenc:index-key"u8);
        return plaintext;
    }

    [Fact]
    public async Task Should_use_blind_index_key_when_it_can_not_be_rewrapped()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var store = new ReadOnlyRootKeyStore();

        await using (var initial = DocumentDbContext.BuildProvider(x => x.UseKey(oldKey).UseRootKeyStore(_ => store)))
            await initial.GetRequiredService<DataKeyRing>().LoadIndexKeysAsync(typeof(DocumentDbContext), CancellationToken.None);

        // a new static key: the index key would be rewrapped with it, but the store can't be written
        store.ReadOnly = true;
        await using var rotated = DocumentDbContext.BuildProvider(x => x.UseKey(oldKey).UseKey(RandomNumberGenerator.GetBytes(32), id: 2).UseRootKeyStore(_ => store));

        var load = () => rotated.GetRequiredService<DataKeyRing>().LoadIndexKeysAsync(typeof(DocumentDbContext), CancellationToken.None);

        await load.Should().NotThrowAsync();
        store.Keys(typeof(DocumentDbContext)).Should().ContainSingle().Which.WrappingKeyId.Should().Be("static:1");
    }

    private sealed class TimingOutProvider : IRootKeyProvider
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public Task<RootKey> GetActiveRootKeyAsync(Type dbContextType, CancellationToken cancellationToken) => Task.FromResult(new RootKey(1, _key));
        public Task<RootKey?> GetRootKeyAsync(Type dbContextType, int rootKeyId, CancellationToken cancellationToken) => Task.FromResult<RootKey?>(new RootKey(1, _key));
        public Task<int?> GetActiveRootKeyIdAsync(Type dbContextType, CancellationToken cancellationToken) => throw new TaskCanceledException("timed out");
        public Task<RootKey> RotateRootKeyAsync(Type dbContextType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RewrapRootKeysAsync(Type dbContextType, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ReadOnlyRootKeyStore : IRootKeyStore
    {
        private readonly InMemoryRootKeyStore _inner = new();

        public bool ReadOnly { get; set; }

        public IReadOnlyList<WrappedRootKey> Keys(Type dbContextType) => _inner.Keys(dbContextType);

        public Task<IReadOnlyList<WrappedRootKey>> GetAllAsync(Type dbContextType, CancellationToken cancellationToken)
            => _inner.GetAllAsync(dbContextType, cancellationToken);

        public Task<bool> TryAddAsync(Type dbContextType, WrappedRootKey rootKey, CancellationToken cancellationToken)
            => _inner.TryAddAsync(dbContextType, rootKey, cancellationToken);

        public Task UpdateAsync(Type dbContextType, WrappedRootKey rootKey, CancellationToken cancellationToken)
            => ReadOnly ? throw new UnauthorizedAccessException("permission denied") : _inner.UpdateAsync(dbContextType, rootKey, cancellationToken);
    }
}
