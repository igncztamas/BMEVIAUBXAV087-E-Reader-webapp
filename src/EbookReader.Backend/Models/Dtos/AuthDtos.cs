namespace EbookReader.Backend.Models;

public record RegisterRequest(string Username, string Password);

public record LoginRequest(string Username, string Password);

public record UserDto(Guid Id, string Username);

public record AuthResponse(string Token, UserDto User);

public record ApiErrorResponse(string Error, string Code);
