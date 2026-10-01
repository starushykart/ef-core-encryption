using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using EntityFrameworkCore.Encrypted.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encrypted.Benchmarks;

/// <summary>Cost of encrypting and decrypting one value through the EF value converters (no database).</summary>
[MemoryDiagnoser]
public class ConverterBenchmarks
{
    private ServiceProvider _provider = null!;
    private ValueConverter _text = null!;
    private ValueConverter _blob = null!;
    private string _plainText = null!;
    private byte[] _plainBlob = null!;
    private string _encryptedText = null!;
    private byte[] _encryptedBlob = null!;

    [Params(32, 1024)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _provider = new ServiceCollection()
            .AddEncryption(x => x.UseKey(RandomNumberGenerator.GetBytes(32)))
            .AddDbContext<BenchmarkDbContext>(x => x.UseSqlite("Data Source=:memory:").UseEncryption())
            .BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var entity = scope.ServiceProvider.GetRequiredService<BenchmarkDbContext>().Model.FindEntityType(typeof(Item))!;
        _text = entity.FindProperty(nameof(Item.Text))!.GetValueConverter()!;
        _blob = entity.FindProperty(nameof(Item.Blob))!.GetValueConverter()!;

        _plainText = new string('a', Size);
        _plainBlob = RandomNumberGenerator.GetBytes(Size);
        _encryptedText = (string)_text.ConvertToProvider(_plainText)!;
        _encryptedBlob = (byte[])_blob.ConvertToProvider(_plainBlob)!;
    }

    [GlobalCleanup]
    public void Cleanup()
        => _provider.Dispose();

    [Benchmark]
    public object? EncryptString() => _text.ConvertToProvider(_plainText);

    [Benchmark]
    public object? DecryptString() => _text.ConvertFromProvider(_encryptedText);

    [Benchmark]
    public object? EncryptBinary() => _blob.ConvertToProvider(_plainBlob);

    [Benchmark]
    public object? DecryptBinary() => _blob.ConvertFromProvider(_encryptedBlob);

    private sealed class BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options) : DbContext(options)
    {
        public DbSet<Item> Items => Set<Item>();
    }

    private sealed class Item
    {
        public int Id { get; set; }

        [Encrypted]
        public string? Text { get; set; }

        [Encrypted]
        public byte[]? Blob { get; set; }
    }
}
