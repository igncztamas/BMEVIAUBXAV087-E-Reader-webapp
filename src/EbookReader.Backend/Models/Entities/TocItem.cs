namespace EbookReader.Backend.Models;

/// <summary>
/// Domain model representing a chapter or section entry in a book's table of contents.
/// </summary>
public class TocItem
{
    /// <summary>Display title of the chapter or section.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>1-based target page number corresponding to this table of contents entry.</summary>
    public int PageNumber { get; set; }

    /// <summary>Hierarchy nesting level (0 for top-level, 1 for subsections, etc.).</summary>
    public int Level { get; set; }
}
