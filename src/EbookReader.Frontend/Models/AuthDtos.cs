namespace EbookReader.Frontend.Models;

/// <summary>
/// Request payload used to register a new user account.
/// </summary>
/// <param name="Username">The requested username (minimum 3 characters).</param>
/// <param name="Password">The plain-text password for account creation (minimum 4 characters).</param>
public record RegisterRequestDto(string Username, string Password);

/// <summary>
/// Request payload used to authenticate an existing user.
/// </summary>
/// <param name="Username">The registered username.</param>
/// <param name="Password">The plain-text password.</param>
public record LoginRequestDto(string Username, string Password);

/// <summary>
/// Basic representation of an authenticated user profile.
/// </summary>
/// <param name="Id">The unique user identifier.</param>
/// <param name="Username">The unique username.</param>
public record UserInfoDto(Guid Id, string Username);

/// <summary>
/// Authentication response containing the JWT bearer token and user summary.
/// </summary>
/// <param name="Token">The JWT bearer token used for authorized API requests.</param>
/// <param name="User">The authenticated user profile information.</param>
public record AuthResponseDto(string Token, UserInfoDto User);

/// <summary>
/// Standard error schema returned by the backend API upon failures.
/// Supports both legacy (Error/Code) and updated (Message/ErrorCode) property namings.
/// </summary>
/// <param name="Error">The human-readable error description.</param>
/// <param name="Code">The machine-readable EbookErrorCodes identifier.</param>
/// <param name="Message">Alternative property name for error description.</param>
/// <param name="ErrorCode">Alternative property name for machine-readable error code.</param>
public record ApiErrorDto(
    string? Error = null,
    string? Code = null,
    string? Message = null,
    string? ErrorCode = null)
{
    /// <summary>
    /// Gets the resolved human-readable error message.
    /// </summary>
    public string? ResolvedError => Error ?? Message;

    /// <summary>
    /// Gets the resolved error code identifier.
    /// </summary>
    public string? ResolvedCode => Code ?? ErrorCode;
}
