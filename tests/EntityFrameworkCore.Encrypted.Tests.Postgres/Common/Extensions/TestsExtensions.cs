using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.Common.Extensions;

public static class TestsExtensions
{
    public static IServiceCollection AddXunitLogging(this IServiceCollection services, ITestOutputHelper helper)
        => services.AddLogging(x => x.AddXUnit(helper));
    
    public static async Task MigrateContextAsync<TContext>(this IServiceProvider provider, bool useFactory) where TContext : DbContext
    {
        await using var scope = provider.CreateAsyncScope();

        if (useFactory)
        {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
            await using var context = await factory.CreateDbContextAsync();
            await context.Database.MigrateAsync();
        }
        else
        {
            await using var context = scope.ServiceProvider.GetRequiredService<TContext>();
            await context.Database.MigrateAsync();
        }
    }
}