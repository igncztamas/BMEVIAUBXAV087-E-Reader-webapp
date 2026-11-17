namespace EbookReader.Backend.Models;

public static class BookMapper
{
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

    public static BookDetailDto ToDetail(this Book book)
    {
        int totalPages = Math.Max(1, book.Pages.Count);
        int currentPage = Math.Clamp(book.CurrentPage, 1, totalPages);
        string currentContent = book.Pages.Count > 0 ? book.Pages[currentPage - 1] : string.Empty;

        var bookmarks = book.Bookmarks
            .OrderBy(b => b.PageNumber)
            .Select(b => new BookmarkDto(b.Id, book.Id, b.Name, b.PageNumber, b.CreatedAtUtc))
            .ToList();

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
