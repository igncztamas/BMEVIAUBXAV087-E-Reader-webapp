namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;

public interface IAuthService
{
    Task<ServiceResult<AuthResponse>> RegisterAsync(string? username, string? password);
    Task<ServiceResult<AuthResponse>> LoginAsync(string? username, string? password);
    Task<UserDto?> ValidateTokenAsync(string? token);
    Task LogoutAsync(string? token);
}
