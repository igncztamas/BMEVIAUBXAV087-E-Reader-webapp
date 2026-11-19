namespace EbookReader.Backend.Models;

/// <summary>
/// In-memory representation of database tables used for thread-safe reads and mutations by <see cref="EbookReader.Backend.Services.IDataStore"/>.
/// </summary>
public class DatabaseSnapshot
{
    /// <summary>Registered users table snapshot.</summary>
    public List<User> Users { get; set; } = new();

    /// <summary>Active session tokens snapshot.</summary>
    public List<SessionToken> Sessions { get; set; } = new();

    /// <summary>User book libraries and embedded bookmarks snapshot.</summary>
    public List<Book> Books { get; set; } = new();
}
