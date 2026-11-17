namespace EbookReader.Frontend.Models;

public record RegisterRequestDto(string Username, string Password);

public record LoginRequestDto(string Username, string Password);

public record UserInfoDto(Guid Id, string Username);

public record AuthResponseDto(string Token, UserInfoDto User);

public record ApiErrorDto(string? Error, string? Code);
