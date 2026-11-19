using EbookReader.Frontend.Models;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace EbookReader.Frontend.Services;

/// <summary>
/// Manages user authentication, theme state, and top navbar headers,
/// persisting critical session credentials into browser ProtectedLocalStorage.
/// </summary>
public class UserSessionService : IUserSessionService
{
    private const string StorageKey = "ebook_auth_session";
    private const string DarkModeStorageKey = "ebook_dark_mode";
    private readonly ProtectedLocalStorage _storage;
    private bool _initialized;
    private Task? _initTask;

    /// <inheritdoc/>
    public string? Token { get; private set; }

    /// <inheritdoc/>
    public UserInfoDto? CurrentUser { get; private set; }

    /// <inheritdoc/>
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token) && CurrentUser is not null;

    /// <inheritdoc/>
    public string HeaderTitle { get; private set; } = "My Library";

    /// <inheritdoc/>
    public bool IsDarkMode { get; private set; }

    /// <inheritdoc/>
    public event Action? OnChange;

    /// <summary>
    /// Initializes a new instance of <see cref="UserSessionService"/> using the specified protected browser storage.
    /// </summary>
    /// <param name="storage">The protected local storage provider.</param>
    public UserSessionService(ProtectedLocalStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
    }

    /// <inheritdoc/>
    public void SetHeaderTitle(string title)
    {
        if (HeaderTitle != title)
        {
            HeaderTitle = title;
            OnChange?.Invoke();
        }
    }

    /// <inheritdoc/>
    public void SetDarkMode(bool isDark)
    {
        if (IsDarkMode != isDark)
        {
            IsDarkMode = isDark;
            OnChange?.Invoke();

            // Persist theme choice asynchronously without blocking UI render
            _ = SaveDarkModeAsync(isDark);
        }
    }

    /// <inheritdoc/>
    public void ToggleDarkMode() => SetDarkMode(!IsDarkMode);

    /// <inheritdoc/>
    public Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        // Reuse in-flight initialization task to avoid concurrent storage reads
        return _initTask ??= InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            // Restore authentication session from protected storage
            var result = await _storage.GetAsync<StoredUserSession>(StorageKey);
            if (!_initialized && result.Success && result.Value is not null && !string.IsNullOrWhiteSpace(result.Value.Token) && result.Value.User is not null)
            {
                Token = result.Value.Token;
                CurrentUser = result.Value.User;
                OnChange?.Invoke();
            }

            try
            {
                // Restore dark mode preference
                var darkResult = await _storage.GetAsync<bool>(DarkModeStorageKey);
                if (darkResult.Success)
                {
                    IsDarkMode = darkResult.Value;
                    OnChange?.Invoke();
                }
            }
            catch
            {
                // Fall back to default light mode if reading theme setting fails
            }
        }
        catch
        {
            // Storage access can fail during prerendering or if user blocks cookies/local storage
        }
        finally
        {
            _initialized = true;
        }
    }

    /// <inheritdoc/>
    public void SetSession(string token, UserInfoDto user)
    {
        Token = token;
        CurrentUser = user;
        _initialized = true;
        _initTask = Task.CompletedTask;
        OnChange?.Invoke();

        // Persist session into encrypted protected browser storage
        _ = SaveStorageAsync(token, user);
    }

    /// <inheritdoc/>
    public void ClearSession()
    {
        Token = null;
        CurrentUser = null;
        _initialized = true;
        _initTask = Task.CompletedTask;
        OnChange?.Invoke();

        // Remove session from browser storage
        _ = DeleteStorageAsync();
    }

    private async Task SaveStorageAsync(string token, UserInfoDto user)
    {
        try
        {
            await _storage.SetAsync(StorageKey, new StoredUserSession(token, user));
        }
        catch
        {
            // Ignore storage errors on disconnect
        }
    }

    private async Task DeleteStorageAsync()
    {
        try
        {
            await _storage.DeleteAsync(StorageKey);
        }
        catch
        {
            // Ignore storage errors on disconnect
        }
    }

    private async Task SaveDarkModeAsync(bool isDark)
    {
        try
        {
            await _storage.SetAsync(DarkModeStorageKey, isDark);
        }
        catch
        {
            // Ignore storage errors on disconnect
        }
    }
}
