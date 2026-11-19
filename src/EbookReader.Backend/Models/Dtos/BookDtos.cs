namespace EbookReader.Backend.Models;

/// <summary>Request payload for uploading a book via Base64 JSON payload.</summary>
public record UploadBookRequest(
    string FileName,
    string FileBytesBase64,
    string? Title = null);

/// <summary>Summary representation of a book used in library listings.</summary>
public record BookSummaryDto(
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

/// <summary>Data transfer object for a user bookmark.</summary>
public record BookmarkDto(
    Guid Id,
    Guid BookId,
    string Name,
    int PageNumber,
    DateTimeOffset CreatedAtUtc);

/// <summary>Data transfer object for a table of contents item.</summary>
public record TocItemDto(string Title, int PageNumber, int Level);

/// <summary>Detailed representation of a book including its current page content, bookmarks, and table of contents.</summary>
public record BookDetailDto(
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
    List<BookmarkDto> Bookmarks,
    string? Author = null,
    int ZoomLevel = 100,
    List<TocItemDto>? TableOfContents = null);

/// <summary>Data transfer object containing content and clamping information for a requested book page.</summary>
public record BookPageDto(
    Guid BookId,
    int RequestedPageNumber,
    int PageNumber,
    int TotalPages,
    string Content,
    bool WasClamped);

/// <summary>Request payload for updating the current reading page and zoom level.</summary>
public record UpdateProgressRequest(
    int PageNumber,
    DateTimeOffset? ClientTimestampUtc = null,
    int? ZoomLevel = null);

/// <summary>Response payload indicating the result of a reading progress update with LWW concurrency info.</summary>
public record UpdateProgressResponse(
    Guid BookId,
    int CurrentPage,
    int TotalPages,
    DateTimeOffset LastPositionUpdatedAtUtc,
    bool WasUpdated,
    bool WasClamped,
    int ZoomLevel = 100);

/// <summary>Request payload for adding a new bookmark.</summary>
public record CreateBookmarkRequest(
    string Name,
    int PageNumber);
