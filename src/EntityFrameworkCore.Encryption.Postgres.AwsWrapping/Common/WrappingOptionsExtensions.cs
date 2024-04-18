namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Common;

public static class WrappingOptionsExtensions
{
    public static WrappingOptions WithKeyArn(this WrappingOptions opt, string? keyArn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyArn);
        opt.WrappingKeyArn = keyArn;
        return opt;
    }
    
    public static WrappingOptions GenerateDataKeyIfNotExist(this WrappingOptions opt, bool value = true)
    {
        opt.GenerateDataKeyIfNotExist = value;
        return opt;
    }
}