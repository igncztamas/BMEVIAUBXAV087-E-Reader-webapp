namespace EbookReader.Backend.Models;

/// <summary>
/// Domain entity representing a registered user with PBKDF2 salted password credentials.
/// </summary>
public class User
{
    /// <summary>Unique identifier for the user.</summary>
    public Guid Id { get; set; }

    /// <summary>Unique username chosen by the user.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Base64-encoded cryptographic salt used for password hashing.</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>Base64-encoded PBKDF2-SHA256 password hash.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the user account was registered.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}
