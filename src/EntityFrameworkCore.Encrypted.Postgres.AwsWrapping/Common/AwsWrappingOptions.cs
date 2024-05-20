using System.ComponentModel.DataAnnotations;

namespace EntityFrameworkCore.Encrypted.Postgres.AwsWrapping.Common;

public class AwsWrappingOptions
{
    [Required]
    public string WrappingKeyArn { get; set; } = null!;
    public bool GenerateDataKeyIfNotExist { get; set; } = true;
    public bool ReEncryptDataKeyOnStart { get; set; } = true;
}