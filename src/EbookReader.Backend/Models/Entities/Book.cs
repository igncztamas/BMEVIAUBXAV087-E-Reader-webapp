namespace EbookReader.Backend.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Domain entity representing an uploaded e-book (PDF or EPUB) with its content, progress, and bookmarks.
/// </summary>
public class Book
{
    /// <summary>Unique identifier for the book.</summary>
    public Guid Id { get; set; }

    /// <summary>Identifier of the user who owns this book.</summary>
    public Guid UserId { get; set; }

    /// <summary>Display title of the book.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Author name extracted from metadata or fallback to filename.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Original uploaded file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Document format ("PDF" or "EPUB").</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>Hex-encoded SHA-256 hash of original file content used for duplicate detection.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>Raw file bytes stored for download or full retrieval.</summary>
    public byte[] FileBytes { get; set; } = Array.Empty<byte>();

    /// <summary>HTML or text content for each extracted page (1-based indexing).</summary>
    public List<string> Pages { get; set; } = new();

    /// <summary>Total number of pages in the book.</summary>
    public int TotalPages { get; set; }

    /// <summary>Current reading page position (1 to TotalPages).</summary>
    public int CurrentPage { get; set; } = 1;

    /// <summary>Reader zoom level percentage (clamped between 50 and 300, default 100).</summary>
    public int ZoomLevel { get; set; } = 100;

    /// <summary>UTC timestamp of the last reading position update for Last-Write-Wins concurrency.</summary>
    public DateTimeOffset LastPositionUpdatedAtUtc { get; set; }

    /// <summary>UTC timestamp when the book was uploaded.</summary>
    public DateTimeOffset UploadedAtUtc { get; set; }

    /// <summary>List of user-created bookmarks associated with this book.</summary>
    public List<Bookmark> Bookmarks { get; set; } = new();

    /// <summary>Hierarchical table of contents extracted from the document.</summary>
    public List<TocItem> TableOfContents { get; set; } = new();

    [JsonIgnore]
    private Action<Book>? _contentLoader;

    /// <summary>Registers an on-demand content loader callback for lazy loading large book data.</summary>
    public void SetContentLoader(Action<Book> loader) => _contentLoader = loader;

    /// <summary>Ensures that full book content (Pages, FileBytes, TableOfContents) is loaded from persistent storage.</summary>
    public void EnsureContentLoaded()
    {
        var loader = _contentLoader;
        _contentLoader = null;
        loader?.Invoke(this);
    }
}
