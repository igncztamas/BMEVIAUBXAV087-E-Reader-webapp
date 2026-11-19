namespace EbookReader.Frontend.Models;

/// <summary>
/// Data contract persisted in browser ProtectedLocalStorage to maintain user sessions across page reloads.
/// </summary>
public class StoredUserSession
{
    /// <summary>
    /// Gets or sets the JWT bearer authentication token.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cached user profile summary.
    /// </summary>
    public UserInfoDto? User { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="StoredUserSession"/> (parameterless for deserialization).
    /// </summary>
    public StoredUserSession() { }

    /// <summary>
    /// Initializes a new instance of <see cref="StoredUserSession"/> with token and user details.
    /// </summary>
    /// <param name="token">The JWT token.</param>
    /// <param name="user">The user profile data.</param>
    public StoredUserSession(string token, UserInfoDto user)
    {
        Token = token;
        User = user;
    }
}
