namespace EbookReader.Backend.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public class SessionToken
{
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
}

public class Book
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty; // "PDF" or "EPUB"
    public string ContentHash { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public List<string> Pages { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public DateTimeOffset LastPositionUpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<Bookmark> Bookmarks { get; set; } = new();
}

public class Bookmark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public class DatabaseSnapshot
{
    public List<User> Users { get; set; } = new();
    public List<Book> Books { get; set; } = new();
    public List<SessionToken> Sessions { get; set; } = new();
}
