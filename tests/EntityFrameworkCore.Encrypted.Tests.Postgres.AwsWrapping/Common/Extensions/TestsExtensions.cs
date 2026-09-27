using Amazon.KeyManagementService;
using Amazon.Runtime;
using EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encrypted.Tests.Postgres.AwsWrapping.Common.Extensions;

public static class TestsExtensions
{
    public static IServiceCollection AddLocalstackKms(this IServiceCollection services, LocalstackContainerFixture localstack)
        => services.AddSingleton<IAmazonKeyManagementService>(new AmazonKeyManagementServiceClient(
            new BasicAWSCredentials("admin", "admin"),
            new AmazonKeyManagementServiceConfig { ServiceURL = localstack.Url }));
}