using System.Security.Cryptography;
using EbookReader.Backend.Models;

namespace EbookReader.Backend.Services;

public class AuthService : IAuthService
{
    private readonly IDataStore _dataStore;

    public AuthService(IDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public async Task<ServiceResult<AuthResponse>> RegisterAsync(string? username, string? password)
    {
        var trimmedUsername = username?.Trim() ?? string.Empty;
        if (trimmedUsername.Length < 3)
        {
            return ServiceResult<AuthResponse>.Failure(
                400,
                EbookErrorCodes.InvalidUsername,
                "Username must be at least 3 characters long.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
        {
            return ServiceResult<AuthResponse>.Failure(
                400,
                EbookErrorCodes.InvalidPassword,
                "Password must be at least 4 characters long.");
        }

        return await _dataStore.WriteAsync(db =>
        {
            var exists = db.Users.Any(u =>
                u.Username.Equals(trimmedUsername, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                return ServiceResult<AuthResponse>.Failure(
                    409,
                    EbookErrorCodes.DuplicateUsername,
                    "This username is already taken. Please choose another one.");
            }

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

            var session = CreateSessionToken(user);
            db.Sessions.Add(session);

            return ServiceResult<AuthResponse>.Success(
                new AuthResponse(session.Token, new UserDto(user.Id, user.Username)),
                201);
        });
    }

    public async Task<ServiceResult<AuthResponse>> LoginAsync(string? username, string? password)
    {
        var trimmedUsername = username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedUsername) || string.IsNullOrWhiteSpace(password))
        {
            return ServiceResult<AuthResponse>.Failure(
                401,
                EbookErrorCodes.InvalidCredentials,
                "Invalid username or password.");
        }

        return await _dataStore.WriteAsync(db =>
        {
            var user = db.Users.FirstOrDefault(u =>
                u.Username.Equals(trimmedUsername, StringComparison.OrdinalIgnoreCase));

            if (user is null)
            {
                return ServiceResult<AuthResponse>.Failure(
                    401,
                    EbookErrorCodes.InvalidCredentials,
                    "Invalid username or password.");
            }

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

            var session = CreateSessionToken(user);
            db.Sessions.Add(session);

            return ServiceResult<AuthResponse>.Success(
                new AuthResponse(session.Token, new UserDto(user.Id, user.Username)));
        });
    }

    public async Task<UserDto?> ValidateTokenAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var normalized = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? token["Bearer ".Length..].Trim()
            : token.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return await _dataStore.ReadAsync(db =>
        {
            var now = DateTimeOffset.UtcNow;
            var session = db.Sessions.FirstOrDefault(s =>
                s.Token.Equals(normalized, StringComparison.Ordinal) &&
                s.ExpiresAtUtc > now);

            return session is null ? null : new UserDto(session.UserId, session.Username);
        });
    }

    public async Task LogoutAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var normalized = token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? token["Bearer ".Length..].Trim()
            : token.Trim();

        await _dataStore.WriteAsync(db =>
        {
            db.Sessions.RemoveAll(s => s.Token.Equals(normalized, StringComparison.Ordinal));
            return true;
        });
    }

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
