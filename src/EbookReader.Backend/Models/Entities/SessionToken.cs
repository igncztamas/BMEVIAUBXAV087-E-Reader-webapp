namespace EbookReader.Backend.Models;

/// <summary>
/// Domain entity representing an active authentication session token for a logged-in user.
/// </summary>
public class SessionToken
{
    /// <summary>Hex-encoded cryptographically secure token string (primary key).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Identifier of the user who owns this session.</summary>
    public Guid UserId { get; set; }

    /// <summary>Username associated with this session token.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the token was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>UTC timestamp after which the token is considered expired.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
