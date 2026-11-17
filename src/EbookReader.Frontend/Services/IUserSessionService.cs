using EbookReader.Frontend.Models;

namespace EbookReader.Frontend.Services;

public interface IUserSessionService
{
    string? Token { get; }
    UserInfoDto? CurrentUser { get; }
    bool IsAuthenticated { get; }
    string HeaderTitle { get; }
    bool IsDarkMode { get; }
    event Action? OnChange;
    Task EnsureInitializedAsync();
    void SetSession(string token, UserInfoDto user);
    void ClearSession();
    void SetHeaderTitle(string title);
    void SetDarkMode(bool isDark);
    void ToggleDarkMode();
}
