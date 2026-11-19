namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;

/// <summary>
/// Service interface handling user registration, password authentication, and session token management.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registers a new user account with PBKDF2-SHA256 salted password hashing.
    /// </summary>
    /// <param name="username">Desired username (minimum 3 characters).</param>
    /// <param name="password">Plaintext password (minimum 4 characters).</param>
    /// <returns>A <see cref="ServiceResult{T}"/> containing the session token and user profile, or failure error code.</returns>
    Task<ServiceResult<AuthResponse>> RegisterAsync(string? username, string? password);

    /// <summary>
    /// Authenticates a user with username and password, generating an active session token upon success.
    /// </summary>
    /// <param name="username">The username to authenticate.</param>
    /// <param name="password">The plaintext password to verify.</param>
    /// <returns>A <see cref="ServiceResult{T}"/> containing the session token and user profile, or 401 InvalidCredentials.</returns>
    Task<ServiceResult<AuthResponse>> LoginAsync(string? username, string? password);

    /// <summary>
    /// Validates an incoming bearer session token and returns the authenticated user if valid and unexpired.
    /// </summary>
    /// <param name="token">Raw or 'Bearer '-prefixed session token string.</param>
    /// <returns>The user DTO if valid; otherwise null.</returns>
    Task<UserDto?> ValidateTokenAsync(string? token);

    /// <summary>
    /// Invalidates an active session token, logging the user out.
    /// </summary>
    /// <param name="token">Raw or 'Bearer '-prefixed session token string.</param>
    Task LogoutAsync(string? token);
}
