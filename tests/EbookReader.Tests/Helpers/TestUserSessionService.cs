using EbookReader.Frontend.Models;
using EbookReader.Frontend.Services;

namespace EbookReader.Tests.Helpers;

/// <summary>
/// In-memory <see cref="IUserSessionService"/> test double without browser JS interop dependencies.
/// </summary>
internal sealed class TestUserSessionService : IUserSessionService
{
    public string? Token { get; private set; }
    public UserInfoDto? CurrentUser { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token) && CurrentUser is not null;
    public string HeaderTitle { get; private set; } = "E-book Reader";
    public bool IsDarkMode { get; private set; }

    public event Action? OnChange;

    public Task EnsureInitializedAsync() => Task.CompletedTask;

    public void SetSession(string token, UserInfoDto user)
    {
        Token = token;
        CurrentUser = user;
        OnChange?.Invoke();
    }

    public void ClearSession()
    {
        Token = null;
        CurrentUser = null;
        OnChange?.Invoke();
    }

    public void SetHeaderTitle(string title)
    {
        HeaderTitle = title;
        OnChange?.Invoke();
    }

    public void SetDarkMode(bool isDark)
    {
        IsDarkMode = isDark;
        OnChange?.Invoke();
    }

    public void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        OnChange?.Invoke();
    }
}
