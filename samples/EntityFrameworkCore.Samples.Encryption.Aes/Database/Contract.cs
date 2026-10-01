using EntityFrameworkCore.Encrypted.Annotations;

namespace EntityFrameworkCore.Samples.Encryption.Aes.Database;

/// <summary>
/// Existing table whose IBANs the previous code encrypted with plain AES-256 (see <c>Legacy</c>):
/// read with the legacy decryptor, migrated to the library's format with <c>POST /keys/re-encrypt</c>.
/// </summary>
public class Contract
{
    public Guid Id { get; set; }

    public string Number { get; set; } = null!;

    [Encrypted]
    public string Iban { get; set; } = null!;
}
