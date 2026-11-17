using EbookReader.Frontend.Models;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace EbookReader.Frontend.Services;

public class UserSessionService : IUserSessionService
{
    private const string StorageKey = "ebook_auth_session";
    private const string DarkModeStorageKey = "ebook_dark_mode";
    private readonly ProtectedLocalStorage _storage;
    private bool _initialized;
    private Task? _initTask;

    public string? Token { get; private set; }
    public UserInfoDto? CurrentUser { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token) && CurrentUser is not null;
    public string HeaderTitle { get; private set; } = "My Library";
    public bool IsDarkMode { get; private set; }

    public event Action? OnChange;

    public UserSessionService(ProtectedLocalStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
    }

    public void SetHeaderTitle(string title)
    {
        if (HeaderTitle != title)
        {
            HeaderTitle = title;
            OnChange?.Invoke();
        }
    }

    public void SetDarkMode(bool isDark)
    {
        if (IsDarkMode != isDark)
        {
            IsDarkMode = isDark;
            OnChange?.Invoke();
            _ = SaveDarkModeAsync(isDark);
        }
    }

    public void ToggleDarkMode() => SetDarkMode(!IsDarkMode);

    public Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        return _initTask ??= InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            var result = await _storage.GetAsync<StoredUserSession>(StorageKey);
            if (!_initialized && result.Success && result.Value is not null && !string.IsNullOrWhiteSpace(result.Value.Token) && result.Value.User is not null)
            {
                Token = result.Value.Token;
                CurrentUser = result.Value.User;
                OnChange?.Invoke();
            }

            try
            {
                var darkResult = await _storage.GetAsync<bool>(DarkModeStorageKey);
                if (darkResult.Success)
                {
                    IsDarkMode = darkResult.Value;
                    OnChange?.Invoke();
                }
            }
            catch
            {
            }
        }
        catch
        {
            // JS interop may fail if called outside an interactive context or during disconnect
        }
        finally
        {
            _initialized = true;
        }
    }

    public void SetSession(string token, UserInfoDto user)
    {
        Token = token;
        CurrentUser = user;
        _initialized = true;
        _initTask = Task.CompletedTask;
        OnChange?.Invoke();
        _ = SaveStorageAsync(token, user);
    }

    public void ClearSession()
    {
        Token = null;
        CurrentUser = null;
        _initialized = true;
        _initTask = Task.CompletedTask;
        OnChange?.Invoke();
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
        }
    }
}
