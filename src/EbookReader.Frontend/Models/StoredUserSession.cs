namespace EbookReader.Frontend.Models;

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
