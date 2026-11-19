namespace EbookReader.Backend.Models;

/// <summary>Request payload for registering a new user account.</summary>
public record RegisterRequest(string Username, string Password);

/// <summary>Request payload for logging into an existing user account.</summary>
public record LoginRequest(string Username, string Password);

/// <summary>Data transfer object representing a authenticated user's public profile.</summary>
public record UserDto(Guid Id, string Username);

/// <summary>Authentication response containing the bearer session token and user profile.</summary>
public record AuthResponse(string Token, UserDto User);

/// <summary>Standard API error response payload containing a message and immutable error code.</summary>
public record ApiErrorResponse(string Message, string ErrorCode);
