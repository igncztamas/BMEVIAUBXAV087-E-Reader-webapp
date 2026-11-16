using EbookReader.Frontend.Models;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace EbookReader.Frontend.Services;

public class StoredUserSession
{
    public string Token { get; set; } = string.Empty;
    public UserInfoDto? User { get; set; }

    public StoredUserSession() { }

    public StoredUserSession(string token, UserInfoDto user)
    {
        Token = token;
        User = user;
    }
}

public interface IUserSessionService
{
    string? Token { get; }
    UserInfoDto? CurrentUser { get; }
    bool IsAuthenticated { get; }
    event Action? OnChange;
    Task EnsureInitializedAsync();
    void SetSession(string token, UserInfoDto user);
    void ClearSession();
}

public class UserSessionService : IUserSessionService
{
    private const string StorageKey = "ebook_auth_session";
    private readonly ProtectedLocalStorage? _storage;
    private bool _initialized;
    private Task? _initTask;

    public string? Token { get; private set; }
    public UserInfoDto? CurrentUser { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token) && CurrentUser is not null;

    public event Action? OnChange;

    public UserSessionService(ProtectedLocalStorage? storage = null)
    {
        _storage = storage;
    }

    public Task EnsureInitializedAsync()
    {
        if (_initialized || _storage is null)
        {
            return Task.CompletedTask;
        }

        return _initTask ??= InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            var result = await _storage!.GetAsync<StoredUserSession>(StorageKey);
            if (!_initialized && result.Success && result.Value is not null && !string.IsNullOrWhiteSpace(result.Value.Token) && result.Value.User is not null)
            {
                Token = result.Value.Token;
                CurrentUser = result.Value.User;
                OnChange?.Invoke();
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

        if (_storage is not null)
        {
            _ = SaveStorageAsync(token, user);
        }
    }

    public void ClearSession()
    {
        Token = null;
        CurrentUser = null;
        _initialized = true;
        _initTask = Task.CompletedTask;
        OnChange?.Invoke();

        if (_storage is not null)
        {
            _ = DeleteStorageAsync();
        }
    }

    private async Task SaveStorageAsync(string token, UserInfoDto user)
    {
        try
        {
            await _storage!.SetAsync(StorageKey, new StoredUserSession(token, user));
        }
        catch
        {
        }
    }

    private async Task DeleteStorageAsync()
    {
        try
        {
            await _storage!.DeleteAsync(StorageKey);
        }
        catch
        {
        }
    }
}
