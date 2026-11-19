namespace EbookReader.Backend.Services;

using System.Security.Cryptography;
using EbookReader.Backend.Models;

/// <summary>
/// Service implementing user registration, PBKDF2-SHA256 password hashing, login verification, and token sessions.
/// </summary>
public class AuthService : IAuthService
{
    private readonly IDataStore _dataStore;

    /// <summary>
    /// Initializes a new instance of <see cref="AuthService"/> with the shared data store.
    /// </summary>
    /// <param name="dataStore">The underlying persistence data store.</param>
    public AuthService(IDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<AuthResponse>> RegisterAsync(string? username, string? password)
    {
        var trimmedUsername = username?.Trim() ?? string.Empty;

        // Validation guard: minimum username length
        if (trimmedUsername.Length < 3)
        {
            return ServiceResult<AuthResponse>.Failure(
                400,
                EbookErrorCodes.InvalidUsername,
                "Username must be at least 3 characters long.");
        }

        // Validation guard: minimum password length
        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
        {
            return ServiceResult<AuthResponse>.Failure(
                400,
                EbookErrorCodes.InvalidPassword,
                "Password must be at least 4 characters long.");
        }

        return await _dataStore.WriteAsync(db =>
        {
            // Check for existing case-insensitive username collision
            var exists = db.Users.Any(u =>
                u.Username.Equals(trimmedUsername, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                return ServiceResult<AuthResponse>.Failure(
                    409,
                    EbookErrorCodes.DuplicateUsername,
                    "This username is already taken. Please choose another one.");
            }

            // Generate unique cryptographic salt and compute PBKDF2 hash
            var saltBytes = RandomNumberGenerator.GetBytes(16);
            var saltBase64 = Convert.ToBase64String(saltBytes);
            var hashBase64 = HashPassword(password, saltBytes);

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = trimmedUsername,
                PasswordSalt = saltBase64,
                PasswordHash = hashBase64,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            db.Users.Add(user);

            // Generate initial session token
            var session = CreateSessionToken(user);
            db.Sessions.Add(session);

            return ServiceResult<AuthResponse>.Success(
                new AuthResponse(session.Token, new UserDto(user.Id, user.Username)),
                201);
        });
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<AuthResponse>> LoginAsync(string? username, string? password)
    {
        var trimmedUsername = username?.Trim() ?? string.Empty;

        // Validation guard: empty credentials check
        if (string.IsNullOrWhiteSpace(trimmedUsername) || string.IsNullOrWhiteSpace(password))
        {
            return ServiceResult<AuthResponse>.Failure(
                401,
                EbookErrorCodes.InvalidCredentials,
                "Invalid username or password.");
        }

        return await _dataStore.WriteAsync(db =>
        {
            // Look up user by username (case-insensitive)
            var user = db.Users.FirstOrDefault(u =>
                u.Username.Equals(trimmedUsername, StringComparison.OrdinalIgnoreCase));

            if (user is null)
            {
                return ServiceResult<AuthResponse>.Failure(
                    401,
                    EbookErrorCodes.InvalidCredentials,
                    "Invalid username or password.");
            }

            // Hash incoming password with stored salt and verify with constant-time equality check
            var saltBytes = Convert.FromBase64String(user.PasswordSalt);
            var expectedHash = HashPassword(password, saltBytes);

            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(user.PasswordHash),
                    Convert.FromBase64String(expectedHash)))
            {
                return ServiceResult<AuthResponse>.Failure(
                    401,
                    EbookErrorCodes.InvalidCredentials,
                    "Invalid username or password.");
            }

            // Create and persist new session token
            var session = CreateSessionToken(user);
            db.Sessions.Add(session);

            return ServiceResult<AuthResponse>.Success(
                new AuthResponse(session.Token, new UserDto(user.Id, user.Username)));
        });
    }

    /// <inheritdoc/>
    public async Task<UserDto?> ValidateTokenAsync(string? token)
    {
        var normalized = NormalizeToken(token);
        if (normalized is null)
        {
            return null;
        }

        return await _dataStore.ReadAsync(db =>
        {
            var now = DateTimeOffset.UtcNow;

            // Find matching token that has not expired
            var session = db.Sessions.FirstOrDefault(s =>
                s.Token.Equals(normalized, StringComparison.Ordinal) &&
                s.ExpiresAtUtc > now);

            return session is null ? null : new UserDto(session.UserId, session.Username);
        });
    }

    /// <inheritdoc/>
    public async Task LogoutAsync(string? token)
    {
        var normalized = NormalizeToken(token);
        if (normalized is null)
        {
            return;
        }

        await _dataStore.WriteAsync(db =>
        {
            // Remove matching session token from active sessions
            db.Sessions.RemoveAll(s => s.Token.Equals(normalized, StringComparison.Ordinal));
            return true;
        });
    }

    /// <summary>
    /// Strips optional 'Bearer ' prefix and whitespace from an authorization token.
    /// </summary>
    private static string? NormalizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var normalized = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? token["Bearer ".Length..].Trim()
            : token.Trim();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    /// <summary>
    /// Creates a new cryptographically random 32-byte session token expiring in 7 days.
    /// </summary>
    private static SessionToken CreateSessionToken(User user)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        return new SessionToken
        {
            Token = Convert.ToHexString(tokenBytes),
            UserId = user.Id,
            Username = user.Username,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
        };
    }

    /// <summary>
    /// Computes a PBKDF2-SHA256 password hash using 10,000 iterations.
    /// </summary>
    private static string HashPassword(string password, byte[] salt)
    {
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations: 10_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);

        return Convert.ToBase64String(hashBytes);
    }
}
