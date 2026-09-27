using Amazon.KeyManagementService;
using EntityFrameworkCore.Encrypted.AwsKms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EntityFrameworkCore.Encrypted;

public static class EncryptionBuilderExtensions
{
    /// <summary>
    /// Envelope encryption with AWS KMS: each encrypted context gets a root key generated and wrapped by KMS and
    /// stored wrapped in its <c>__EncryptionKeys</c> table. Startup costs one KMS <c>Decrypt</c> per context.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="IAmazonKeyManagementService"/> from dependency injection when registered,
    /// otherwise registers it from the AWS configuration (<c>AddAWSService</c>).
    /// </remarks>
    public static EncryptionBuilder UseAwsKms(this EncryptionBuilder builder, Action<AwsKmsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new AwsKmsOptions();
        configure(options);
        options.Validate();

        builder.Services.TryAddAWSService<IAmazonKeyManagementService>();
        builder.Services.TryAddSingleton(options);

        return builder.UseKeyWrapper<AwsKmsKeyWrapper>();
    }

    /// <inheritdoc cref="UseAwsKms(EncryptionBuilder, Action{AwsKmsOptions})"/>
    /// <param name="keyId">KMS key (ARN, id or alias) that wraps new root keys.</param>
    public static EncryptionBuilder UseAwsKms(this EncryptionBuilder builder, string keyId)
        => builder.UseAwsKms(x => x.WithKeyId(keyId));
}
