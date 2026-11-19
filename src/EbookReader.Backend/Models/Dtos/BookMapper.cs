namespace EbookReader.Backend.Models;

/// <summary>
/// Extension methods for mapping <see cref="Book"/> domain entities to summary and detail DTOs.
/// </summary>
public static class BookMapper
{
    /// <summary>
    /// Maps a <see cref="Book"/> domain entity to a lightweight <see cref="BookSummaryDto"/>.
    /// </summary>
    /// <param name="book">The book entity to map.</param>
    /// <returns>A populated summary DTO.</returns>
    public static BookSummaryDto ToSummary(this Book book) => new(
        book.Id,
        book.UserId,
        book.Title,
        book.FileName,
        book.Format,
        book.FileSizeBytes,
        book.Pages.Count,
        book.CurrentPage,
        book.LastPositionUpdatedAtUtc,
        book.UploadedAtUtc,
        book.Bookmarks.Count,
        string.IsNullOrWhiteSpace(book.Author) ? book.FileName : book.Author,
        book.ZoomLevel <= 0 ? 100 : book.ZoomLevel);

    /// <summary>
    /// Maps a <see cref="Book"/> domain entity to a detailed <see cref="BookDetailDto"/> including current page content, bookmarks, and TOC.
    /// </summary>
    /// <param name="book">The book entity to map.</param>
    /// <returns>A populated detail DTO.</returns>
    public static BookDetailDto ToDetail(this Book book)
    {
        // Clamp current page within valid bounds (1 to TotalPages)
        int totalPages = Math.Max(1, book.Pages.Count);
        int currentPage = Math.Clamp(book.CurrentPage, 1, totalPages);
        string currentContent = book.Pages.Count > 0 ? book.Pages[currentPage - 1] : string.Empty;

        // Order bookmarks by page number for reader display
        var bookmarks = book.Bookmarks
            .OrderBy(b => b.PageNumber)
            .Select(b => new BookmarkDto(b.Id, book.Id, b.Name, b.PageNumber, b.CreatedAtUtc))
            .ToList();

        // Project table of contents items
        var toc = (book.TableOfContents ?? new List<TocItem>())
            .Select(t => new TocItemDto(t.Title, t.PageNumber, t.Level))
            .ToList();

        return new BookDetailDto(
            book.Id,
            book.UserId,
            book.Title,
            book.FileName,
            book.Format,
            book.FileSizeBytes,
            totalPages,
            currentPage,
            currentContent,
            book.LastPositionUpdatedAtUtc,
            book.UploadedAtUtc,
            bookmarks,
            string.IsNullOrWhiteSpace(book.Author) ? book.FileName : book.Author,
            book.ZoomLevel <= 0 ? 100 : book.ZoomLevel,
            toc);
    }
}
