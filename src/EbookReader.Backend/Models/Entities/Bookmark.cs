namespace EbookReader.Backend.Models;

/// <summary>
/// Domain entity representing a user-created bookmark pointing to a specific page in a book.
/// </summary>
public class Bookmark
{
    /// <summary>Unique identifier for the bookmark.</summary>
    public Guid Id { get; set; }

    /// <summary>Identifier of the book to which this bookmark belongs.</summary>
    public Guid BookId { get; set; }

    /// <summary>User-provided display name or note for the bookmark.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>1-based page number where the bookmark is placed.</summary>
    public int PageNumber { get; set; }

    /// <summary>UTC timestamp when the bookmark was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}
