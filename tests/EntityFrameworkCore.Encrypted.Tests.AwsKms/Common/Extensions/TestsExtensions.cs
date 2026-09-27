using Amazon.KeyManagementService;
using Amazon.Runtime;
using EntityFrameworkCore.Encrypted.Tests.AwsKms.Common.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Encrypted.Tests.AwsKms.Common.Extensions;

public static class TestsExtensions
{
    public static IServiceCollection AddLocalstackKms(this IServiceCollection services, LocalstackContainerFixture localstack)
        => services.AddSingleton(CreateLocalstackKmsClient(localstack));

    public static IAmazonKeyManagementService CreateLocalstackKmsClient(LocalstackContainerFixture localstack)
        => new AmazonKeyManagementServiceClient(
            new BasicAWSCredentials("admin", "admin"),
            new AmazonKeyManagementServiceConfig { ServiceURL = localstack.Url });
}
