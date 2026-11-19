using EbookReader.Frontend.Models;

namespace EbookReader.Frontend.Services;

/// <summary>
/// Service interface for managing client authentication state, user identity, theme preferences, and header text.
/// </summary>
public interface IUserSessionService
{
    /// <summary>
    /// Gets the current JWT bearer token, or null if the user is unauthenticated.
    /// </summary>
    string? Token { get; }

    /// <summary>
    /// Gets the currently authenticated user's profile information.
    /// </summary>
    UserInfoDto? CurrentUser { get; }

    /// <summary>
    /// Gets a value indicating whether a valid token and user profile are loaded.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the display title shown in the top navigation bar.
    /// </summary>
    string HeaderTitle { get; }

    /// <summary>
    /// Gets a value indicating whether dark theme is currently enabled.
    /// </summary>
    bool IsDarkMode { get; }

    /// <summary>
    /// Occurs when any session state (auth, title, theme) changes.
    /// </summary>
    event Action? OnChange;

    /// <summary>
    /// Asynchronously hydrates the session state from browser storage if not already loaded.
    /// </summary>
    /// <returns>A task representing the initialization operation.</returns>
    Task EnsureInitializedAsync();

    /// <summary>
    /// Stores the authentication session in memory and persists it to browser storage.
    /// </summary>
    /// <param name="token">The JWT bearer token.</param>
    /// <param name="user">The user profile data.</param>
    void SetSession(string token, UserInfoDto user);

    /// <summary>
    /// Clears the current authentication session from memory and browser storage.
    /// </summary>
    void ClearSession();

    /// <summary>
    /// Updates the top navigation bar title.
    /// </summary>
    /// <param name="title">The new title string.</param>
    void SetHeaderTitle(string title);

    /// <summary>
    /// Sets the dark mode theme preference and persists it to browser storage.
    /// </summary>
    /// <param name="isDark">True for dark mode; false for light mode.</param>
    void SetDarkMode(bool isDark);

    /// <summary>
    /// Toggles the dark mode state between light and dark.
    /// </summary>
    void ToggleDarkMode();
}
