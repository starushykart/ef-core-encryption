using Amazon.KeyManagementService;
using Amazon.Runtime;
using EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Internal;
using EntityFrameworkCore.Encrypted.Tests.Postgres.Shared.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Sdk;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common.Extensions;

public static class TestsExtensions
{
    public static IServiceCollection AddLocalstackKms(this IServiceCollection services, LocalstackContainerFixture localstack)
        => services.AddSingleton<IAmazonKeyManagementService>(new AmazonKeyManagementServiceClient(
            new BasicAWSCredentials("admin", "admin"),
            new AmazonKeyManagementServiceConfig { ServiceURL = localstack.Url }));

    public static async Task RunAwsWrappingHostedServiceAsync(this IServiceProvider provider)
    {
        var wrappingService = provider.GetServices<IHostedService>().Single();
        
        if (wrappingService is not AwsKeyWrappingHostedService)
            throw new TestClassException($"Cannot resolve {nameof(AwsKeyWrappingHostedService)}");
        
        await wrappingService.StartAsync(CancellationToken.None);
    }
}