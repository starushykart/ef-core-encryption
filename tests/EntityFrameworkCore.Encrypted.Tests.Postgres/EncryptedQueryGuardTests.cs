using System.Security.Cryptography;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Common.TestContext;
using AwesomeAssertions;
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
            { "binary sequence equal", q => q.Where(x => x.Blob!.SequenceEqual(bytes)) },
            { "binary contains byte", q => q.Where(x => x.Blob!.Contains((byte)1)) },
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
            { "any", q => q.Where(_ => q.Any(y => y.Text == value)) },
            { "distinct projection", q => q.Select(x => x.Text).Distinct() },
            { "distinct anonymous projection", q => q.Select(x => new { x.Id, x.Text }).Distinct() },
            { "union projection", q => q.Select(x => x.Text).Union(q.Select(x => x.Other)) },
            { "order projection", q => q.Select(x => x.Text).Order() },
            { "order projection by itself", q => q.Select(x => x.Text).OrderBy(x => x) },
            { "filter projection", q => q.Select(x => x.Text).Where(x => x == value) },
            { "filter projection after take", q => q.Select(x => x.Text).Take(10).Where(x => x!.StartsWith(value)) },
            { "group projection", q => q.Select(x => x.Blob).GroupBy(x => x).Select(x => x.Key) },
            { "projection contains", q => q.Where(_ => q.Select(y => y.Text).Contains(value)) },
            { "union with encrypted second operand", q => q.Select(x => x.Id.ToString()).Union(q.Select(x => x.Text)) },
            { "except with encrypted second operand", q => q.Select(x => x.Id.ToString()).Except(q.Select(x => x.Text!)) },
            { "join on encrypted inner sequence", q => q.Join(q.Select(x => x.Text), x => x.Id.ToString(), s => s, (x, s) => x.Id) },
            { "join on encrypted outer key", q => q.Select(x => x.Text).Join(q, s => s, x => x.Id.ToString(), (s, x) => x.Id) },
            { "nested projection distinct", q => q.Select(x => x.Text).Select(s => s).Distinct() },
            { "nested projection filter", q => q.Select(x => x.Text).Select(s => s).Where(s => s == value) },
            { "nested anonymous projection distinct", q => q.Select(x => x.Text).Select(s => new { s }).Distinct() },
            { "coalesce", q => q.Where(x => (x.Text ?? "") == value) },
            { "order by coalesce", q => q.OrderBy(x => x.Text ?? "") },
            { "conditional", q => q.Where(x => (x.Id > 1 ? x.Text : "a") == value) },
            { "concatenation", q => q.Where(x => x.Text + "" == value) },
            { "scalar subquery", q => q.Where(x => q.Where(y => y.Id == x.Id).Select(y => y.Text).FirstOrDefault() == value) },
            { "plain column compared to subquery", q => q.Where(x => x.Title == q.Select(y => y.Text).FirstOrDefault()) },
            { "anonymous projection filter", q => q.Select(x => new { x.Id, x.Text }).Where(a => a.Text == value) },
            { "dto projection filter", q => q.Select(x => new DocumentDto { Id = x.Id, Text = x.Text }).Where(a => a.Text == value) },
            { "record projection order", q => q.Select(x => new DocumentRecord(x.Id, x.Text)).OrderBy(a => a.Text) },
            { "let", q => from x in q let s = x.Text where s == value select x },
            { "group element max", q => q.GroupBy(x => x.Id, x => x.Text).Select(g => g.Max()) },
            { "group element distinct", q => q.GroupBy(x => 1, x => x.Text).Select(g => g.Distinct().Count()) }
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

    [Fact]
    public void Should_reject_max_over_projected_encrypted_column()
    {
        var act = () => _context.Documents.Select(x => x.Text).Max();

        act.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("Document.Text is encrypted*");
    }

    public static TheoryData<string, Func<IQueryable<Document>, IQueryable>> Allowed()
        => new()
        {
            { "is null", q => q.Where(x => x.Text == null) },
            { "is not null", q => q.Where(x => null != x.Blob) },
            { "projection", q => q.Where(x => x.Id > 1).Select(x => new { x.Text, x.Blob }) },
            { "order by other column", q => q.OrderBy(x => x.Id).Select(x => x.Text) },
            { "group by other column", q => q.GroupBy(x => x.Id).Select(x => x.Key) },
            { "client projection", q => q.Select(x => Mask(x.Text)) },
            { "filter projection by null", q => q.Select(x => x.Text).Where(x => x != null) },
            { "distinct other column", q => q.Select(x => x.Id).Distinct() },
            { "take projection", q => q.OrderBy(x => x.Id).Select(x => x.Text).Take(5) },
            { "anonymous projection filtered by other column", q => q.Select(x => new { x.Id, x.Text }).Where(x => x.Id > 1) },
            { "union of other columns", q => q.Select(x => x.Id).Union(q.Select(x => x.Id)) },
            { "join projecting encrypted inner value", q => q.Join(q, x => x.Id, y => y.Id, (x, y) => y.Text) },
            { "nested projection", q => q.Select(x => x.Text).Select(s => s) },
            { "anonymous projection filtered by null", q => q.Select(x => new { x.Id, x.Text }).Where(a => a.Text == null) },
            { "group element count", q => q.GroupBy(x => x.Id, x => x.Text).Select(g => new { g.Key, Count = g.Count() }) },
            { "let filtered by other column", q => from x in q let s = x.Text where x.Id > 1 select s },
            { "plain column compared", q => q.Where(x => x.Title == "a") }
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

    [Fact]
    public void Should_reject_execute_update_copying_ciphertext()
    {
        var toPlainColumn = () => _context.Documents.ExecuteUpdate(s => s.SetProperty(x => x.Title, x => x.Text));
        var fromColumn = () => _context.Documents.ExecuteUpdate(s => s.SetProperty(x => x.Text, x => x.Title));

        toPlainColumn.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("Document.Text is encrypted and can't be copied*");
        fromColumn.Should().Throw<EntityFrameworkEncryptionException>().WithMessage("Document.Text is encrypted and can only be set to a value*");
    }

    private static string? Mask(string? value)
        => value?[..1];

    public sealed class DocumentDto
    {
        public int Id { get; init; }
        public string? Text { get; init; }
    }

    public sealed record DocumentRecord(int Id, string? Text);
}
