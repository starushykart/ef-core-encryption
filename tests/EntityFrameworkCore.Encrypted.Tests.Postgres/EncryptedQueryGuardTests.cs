using System.Security.Cryptography;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres;

public class EncryptedQueryGuardTests : IDisposable
{
    private static readonly string[] Values = ["a", "b"];

    private readonly ServiceProvider _provider = DocumentDbContext.BuildProvider(x => x.UseKey(RandomNumberGenerator.GetBytes(32)));
    private readonly IServiceScope _scope;
    private readonly DocumentDbContext _context;

    public EncryptedQueryGuardTests()
    {
        _scope = _provider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
    }

    public static TheoryData<string, Func<IQueryable<Document>, IQueryable>> Rejected()
    {
        var value = "secret";
        var bytes = new byte[] { 1 };

        return new()
        {
            { "equal constant", q => q.Where(x => x.Text == "secret") },
            { "equal parameter", q => q.Where(x => x.Text == value) },
            { "not equal", q => q.Where(x => x.Text != value) },
            { "two encrypted columns", q => q.Where(x => x.Text == x.Other) },
            { "binary", q => q.Where(x => x.Blob == bytes) },
            { "binary length", q => q.Where(x => x.Blob!.Length > 1) },
            { "string method", q => q.Where(x => x.Text!.StartsWith(value)) },
            { "string length", q => q.Where(x => x.Text!.Length > 1) },
            { "is null or empty", q => q.Where(x => string.IsNullOrEmpty(x.Text)) },
            { "in list", q => q.Where(x => Values.Contains(x.Text)) },
            { "like", q => q.Where(x => EF.Functions.Like(x.Text!, "%a%")) },
            { "EF.Property", q => q.Where(x => EF.Property<string>(x, nameof(Document.Text)) == value) },
            { "order by", q => q.OrderBy(x => x.Text) },
            { "then by", q => q.OrderBy(x => x.Id).ThenByDescending(x => x.Other) },
            { "group by", q => q.GroupBy(x => x.Text).Select(x => x.Key) },
            { "group by composite key", q => q.GroupBy(x => new { x.Id, x.Text }).Select(x => x.Key) },
            { "projected comparison", q => q.Select(x => x.Text == value) },
            { "any", q => q.Where(_ => q.Any(y => y.Text == value)) }
        };
    }

    [Theory]
    [MemberData(nameof(Rejected))]
    public void Should_reject_queries_evaluated_on_ciphertext(string _, Func<IQueryable<Document>, IQueryable> query)
    {
        var act = () => query(_context.Documents).ToQueryString();

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("Document.* is encrypted and can't be compared*");
    }

    [Fact]
    public void Should_reject_max_over_encrypted_column()
    {
        var act = () => _context.Documents.Max(x => x.Text);

        act.Should().Throw<EntityFrameworkEncryptionException>();
    }

    public static TheoryData<string, Func<IQueryable<Document>, IQueryable>> Allowed()
        => new()
        {
            { "is null", q => q.Where(x => x.Text == null) },
            { "is not null", q => q.Where(x => null != x.Blob) },
            { "projection", q => q.Where(x => x.Id > 1).Select(x => new { x.Text, x.Blob }) },
            { "order by other column", q => q.OrderBy(x => x.Id).Select(x => x.Text) },
            { "group by other column", q => q.GroupBy(x => x.Id).Select(x => x.Key) },
            { "client projection", q => q.Select(x => Mask(x.Text)) }
        };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Should_allow_null_checks_and_projections(string _, Func<IQueryable<Document>, IQueryable> query)
    {
        var act = () => query(_context.Documents).ToQueryString();

        act.Should().NotThrow();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    private static string? Mask(string? value)
        => value?[..1];
}
