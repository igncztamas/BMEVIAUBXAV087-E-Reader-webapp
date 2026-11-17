namespace EbookReader.Backend.Models;

public record UploadBookRequest(
    string FileName,
    string FileBytesBase64,
    string? Title = null);

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

public record BookmarkDto(
    Guid Id,
    Guid BookId,
    string Name,
    int PageNumber,
    DateTimeOffset CreatedAtUtc);

public record TocItemDto(string Title, int PageNumber, int Level);

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

public record BookPageDto(
    Guid BookId,
    int RequestedPageNumber,
    int PageNumber,
    int TotalPages,
    string Content,
    bool WasClamped);

public record UpdateProgressRequest(
    int PageNumber,
    DateTimeOffset? ClientTimestampUtc = null,
    int? ZoomLevel = null);

public record UpdateProgressResponse(
    Guid BookId,
    int CurrentPage,
    int TotalPages,
    DateTimeOffset LastPositionUpdatedAtUtc,
    bool WasUpdated,
    bool WasClamped,
    int ZoomLevel = 100);

public record CreateBookmarkRequest(
    string Name,
    int PageNumber);
