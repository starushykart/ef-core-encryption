using System.ComponentModel.DataAnnotations;

namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common;

public class WrappingOptions
{
    [Required]
    public string WrappingKeyArn { get; set; } = null!;
    public int MaxInitializationRetryCount { get; set; } = 5;
    public bool GenerateDataKeyIfNotExist { get; set; } = true;
    public bool ReEncryptDataKeyOnStart { get; set; } = true;
}