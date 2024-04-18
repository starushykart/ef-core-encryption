using System.ComponentModel.DataAnnotations;

namespace EntityFrameworkCore.Encryption.Postgres.AwsWrapping.Common;

public class WrappingOptions
{
    [Required]
    public string WrappingKeyArn { get; set; } = null!;
    public bool GenerateDataKeyIfNotExist { get; set; } = true;
}