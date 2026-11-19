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

    /// <summary>
    /// Completes immediately since in-memory test sessions do not restore from browser storage.
    /// </summary>
    public Task EnsureInitializedAsync() => Task.CompletedTask;

    /// <summary>
    /// Stores the authenticated session token and user details and notifies subscribers.
    /// </summary>
    public void SetSession(string token, UserInfoDto user)
    {
        Token = token;
        CurrentUser = user;
        OnChange?.Invoke();
    }

    /// <summary>
    /// Clears the current session credentials and notifies subscribers.
    /// </summary>
    public void ClearSession()
    {
        Token = null;
        CurrentUser = null;
        OnChange?.Invoke();
    }

    /// <summary>
    /// Updates the displayed header title and notifies subscribers.
    /// </summary>
    public void SetHeaderTitle(string title)
    {
        HeaderTitle = title;
        OnChange?.Invoke();
    }

    /// <summary>
    /// Explicitly sets dark mode state and notifies subscribers.
    /// </summary>
    public void SetDarkMode(bool isDark)
    {
        IsDarkMode = isDark;
        OnChange?.Invoke();
    }

    /// <summary>
    /// Toggles dark mode state and notifies subscribers.
    /// </summary>
    public void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        OnChange?.Invoke();
    }
}
