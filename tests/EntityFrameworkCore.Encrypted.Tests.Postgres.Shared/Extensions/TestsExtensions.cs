using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.TestContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.Extensions;

public static class TestsExtensions
{
    public static IServiceCollection AddXunitLogging(this IServiceCollection services, ITestOutputHelper helper)
        => services.AddLogging(x => x.AddXUnit(helper));
    
    public static async Task MigrateTestContextAsync(this IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await using var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await context.Database.MigrateAsync();
    }
}