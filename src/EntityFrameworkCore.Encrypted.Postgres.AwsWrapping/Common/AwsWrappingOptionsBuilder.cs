using EntityFrameworkCore.Encrypted.Common.Exceptions;

namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common;

public class AwsWrappingOptionsBuilder
{
    private readonly AwsWrappingOptions _options = new();
    
    public AwsWrappingOptionsBuilder WithKeyArn(string keyArn)
    {
        _options.WrappingKeyArn = keyArn;
        return this;
    }
    
    public AwsWrappingOptionsBuilder GenerateDataKeyIfNotExist(bool value = true)
    {
        _options.GenerateDataKeyIfNotExist = value;
        return this;
    }
    
    public AwsWrappingOptionsBuilder ReEncryptDataKeyOnStart(bool value = true)
    {
        _options.ReEncryptDataKeyOnStart = value;
        return this;
    }

    public AwsWrappingOptions Build()
    {
        if (string.IsNullOrEmpty(_options.WrappingKeyArn))
            throw new EntityFrameworkEncryptionException($"Aws Wrapping Options {nameof(AwsWrappingOptions.WrappingKeyArn)} is not configured");
        
        return _options;
    }
}