using EntityFrameworkCore.Encrypted.Annotations;

namespace EntityFrameworkCore.Samples.Encryption.Aes.Database;

public class Customer
{
    public Guid Id { get; set; }

    /// <summary>Not encrypted: can be filtered and sorted in queries.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Encrypted with the attribute.</summary>
    [Encrypted]
    public string Email { get; set; } = null!;

    /// <summary>Encrypted with <c>IsEncrypted()</c> in <see cref="EncryptedDbContext"/>.</summary>
    public string? Phone { get; set; }

    /// <summary>Enum converted to text, then encrypted.</summary>
    public CustomerStatus Status { get; set; }

    /// <summary>Date converted to text, then encrypted.</summary>
    public DateOnly BirthDate { get; set; }

    /// <summary>Complex type: only the street is encrypted.</summary>
    public Address Address { get; set; } = new();

    /// <summary>Binary data, stored encrypted as binary.</summary>
    [Encrypted]
    public byte[]? Passport { get; set; }
}

public class Address
{
    [Encrypted]
    public string Street { get; set; } = null!;

    public string City { get; set; } = null!;
}

public enum CustomerStatus
{
    Active,
    Blocked
}
