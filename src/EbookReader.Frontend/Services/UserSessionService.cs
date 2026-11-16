using EbookReader.Frontend.Models;

namespace EbookReader.Frontend.Services;

public interface IUserSessionService
{
    string? Token { get; }
    UserInfoDto? CurrentUser { get; }
    bool IsAuthenticated { get; }
    event Action? OnChange;
    void SetSession(string token, UserInfoDto user);
    void ClearSession();
}

public class UserSessionService : IUserSessionService
{
    public string? Token { get; private set; }
    public UserInfoDto? CurrentUser { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token) && CurrentUser is not null;

    public event Action? OnChange;

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
}
