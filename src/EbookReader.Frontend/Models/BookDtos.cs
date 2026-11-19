namespace EbookReader.Frontend.Models;

/// <summary>
/// Request payload used to upload a new e-book file (PDF or EPUB).
/// </summary>
/// <param name="FileName">The original file name including extension.</param>
/// <param name="FileBytesBase64">The base64-encoded raw binary content of the book.</param>
/// <param name="Title">Optional user-provided title; if omitted, metadata or file name is used.</param>
public record UploadBookRequestDto(
    string FileName,
    string FileBytesBase64,
    string? Title = null);

/// <summary>
/// Summary representation of a book displayed in the personal library grid.
/// </summary>
/// <param name="Id">The unique book identifier.</param>
/// <param name="UserId">The owner user ID.</param>
/// <param name="Title">The title of the book.</param>
/// <param name="FileName">The original uploaded file name.</param>
/// <param name="Format">The book format ("PDF" or "EPUB").</param>
/// <param name="FileSizeBytes">The size of the book file in bytes.</param>
/// <param name="TotalPages">The total number of pages or chapters.</param>
/// <param name="CurrentPage">The last recorded reading position page.</param>
/// <param name="LastPositionUpdatedAtUtc">The timestamp of the last reading progress save.</param>
/// <param name="UploadedAtUtc">The timestamp when the book was uploaded.</param>
/// <param name="BookmarkCount">The total number of bookmarks saved for this book.</param>
/// <param name="Author">Optional author name parsed from metadata.</param>
/// <param name="ZoomLevel">The user's preferred zoom level percentage (default: 100).</param>
public record BookSummaryItemDto(
    Guid Id,
    Guid UserId,
    string Title,
    string FileName,
    string Format,
    long FileSizeBytes,
    int TotalPages,
    int CurrentPage,
    DateTimeOffset LastPositionUpdatedAtUtc,
    DateTimeOffset UploadedAtUtc,
    int BookmarkCount,
    string? Author = null,
    int ZoomLevel = 100);

/// <summary>
/// Represents a user-saved bookmark pointing to a specific page in a book.
/// </summary>
/// <param name="Id">The unique bookmark identifier.</param>
/// <param name="BookId">The associated book identifier.</param>
/// <param name="Name">The custom name or label for the bookmark.</param>
/// <param name="PageNumber">The page number associated with the bookmark.</param>
/// <param name="CreatedAtUtc">The timestamp when the bookmark was created.</param>
public record BookmarkItemDto(
    Guid Id,
    Guid BookId,
    string Name,
    int PageNumber,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Represents an entry in a document's hierarchical Table of Contents.
/// </summary>
/// <param name="Title">The title of the section or chapter.</param>
/// <param name="PageNumber">The destination page number.</param>
/// <param name="Level">The indentation hierarchy level (0 for top-level).</param>
public record TocItemDto(string Title, int PageNumber, int Level);

/// <summary>
/// Comprehensive book details including active page content, bookmarks, and Table of Contents.
/// </summary>
/// <param name="Id">The unique book identifier.</param>
/// <param name="UserId">The owner user ID.</param>
/// <param name="Title">The title of the book.</param>
/// <param name="FileName">The original file name.</param>
/// <param name="Format">The book format ("PDF" or "EPUB").</param>
/// <param name="FileSizeBytes">The total size in bytes.</param>
/// <param name="TotalPages">The total number of pages or chapters.</param>
/// <param name="CurrentPage">The current reading position page.</param>
/// <param name="CurrentPageContent">The fallback text/HTML content for the current page.</param>
/// <param name="LastPositionUpdatedAtUtc">The timestamp of the latest progress update.</param>
/// <param name="UploadedAtUtc">The upload timestamp.</param>
/// <param name="Bookmarks">The list of bookmarks created for this book.</param>
/// <param name="Author">The book author if available.</param>
/// <param name="ZoomLevel">The persisted zoom level percentage.</param>
/// <param name="TableOfContents">The hierarchical Table of Contents entries if available.</param>
public record BookDetailItemDto(
    Guid Id,
    Guid UserId,
    string Title,
    string FileName,
    string Format,
    long FileSizeBytes,
    int TotalPages,
    int CurrentPage,
    string CurrentPageContent,
    DateTimeOffset LastPositionUpdatedAtUtc,
    DateTimeOffset UploadedAtUtc,
    List<BookmarkItemDto> Bookmarks,
    string? Author = null,
    int ZoomLevel = 100,
    List<TocItemDto>? TableOfContents = null);

/// <summary>
/// Response payload when navigating to a specific page of a book.
/// </summary>
/// <param name="BookId">The unique book identifier.</param>
/// <param name="RequestedPageNumber">The raw page number that was requested.</param>
/// <param name="PageNumber">The actual clamped page number that was resolved.</param>
/// <param name="TotalPages">The total number of pages in the book.</param>
/// <param name="Content">The fallback text/HTML content of the page.</param>
/// <param name="WasClamped">True if the requested page was out of bounds and clamped.</param>
public record BookPageItemDto(
    Guid BookId,
    int RequestedPageNumber,
    int PageNumber,
    int TotalPages,
    string Content,
    bool WasClamped);

/// <summary>
/// Request payload used to update the current reading position and zoom level.
/// </summary>
/// <param name="PageNumber">The new reading page number.</param>
/// <param name="ClientTimestampUtc">Client timestamp for Last-Write-Wins conflict resolution.</param>
/// <param name="ZoomLevel">Optional zoom percentage to persist.</param>
public record UpdateProgressRequestDto(
    int PageNumber,
    DateTimeOffset? ClientTimestampUtc = null,
    int? ZoomLevel = null);

/// <summary>
/// Response payload returned after updating reading progress.
/// </summary>
/// <param name="BookId">The book identifier.</param>
/// <param name="CurrentPage">The resulting current page number.</param>
/// <param name="TotalPages">The total page count.</param>
/// <param name="LastPositionUpdatedAtUtc">The effective Last-Write-Wins timestamp.</param>
/// <param name="WasUpdated">True if this update took precedence over prior updates.</param>
/// <param name="WasClamped">True if the position was clamped to [1, TotalPages].</param>
/// <param name="ZoomLevel">The resulting zoom level.</param>
public record UpdateProgressResponseDto(
    Guid BookId,
    int CurrentPage,
    int TotalPages,
    DateTimeOffset LastPositionUpdatedAtUtc,
    bool WasUpdated,
    bool WasClamped,
    int ZoomLevel = 100);

/// <summary>
/// Request payload used to create a new bookmark.
/// </summary>
/// <param name="Name">The label for the bookmark.</param>
/// <param name="PageNumber">The target page number.</param>
public record CreateBookmarkRequestDto(
    string Name,
    int PageNumber);
