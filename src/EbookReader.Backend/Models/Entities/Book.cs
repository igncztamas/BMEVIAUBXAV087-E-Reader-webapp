namespace EbookReader.Backend.Models;

public class Book
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty; // "PDF" or "EPUB"
    public string ContentHash { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public byte[] FileBytes { get; set; } = Array.Empty<byte>();
    public List<string> Pages { get; set; } = new();
    public int CurrentPage { get; set; } = 1;
    public int ZoomLevel { get; set; } = 100;
    public DateTimeOffset LastPositionUpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<Bookmark> Bookmarks { get; set; } = new();
    public List<TocItem> TableOfContents { get; set; } = new();
}
