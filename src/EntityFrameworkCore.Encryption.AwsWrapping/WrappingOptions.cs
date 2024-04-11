using System.ComponentModel.DataAnnotations;

namespace EntityFrameworkCore.Encryption.AwsWrapping;

public class WrappingOptions
{
    [Required]
    public string WrappingKeyArn { get; set; } = null!;
    public TimeSpan DataKeyCacheExpiration { get; set; }
    public bool GenerateDataKeyIfNotExist { get; set; }
}