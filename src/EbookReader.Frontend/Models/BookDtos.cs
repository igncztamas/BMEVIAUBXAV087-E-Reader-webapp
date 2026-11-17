namespace EbookReader.Frontend.Models;

public record UploadBookRequestDto(
    string FileName,
    string FileBytesBase64,
    string? Title = null);

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

public record BookmarkItemDto(
    Guid Id,
    Guid BookId,
    string Name,
    int PageNumber,
    DateTimeOffset CreatedAtUtc);

public record TocItemDto(string Title, int PageNumber, int Level);

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

public record BookPageItemDto(
    Guid BookId,
    int RequestedPageNumber,
    int PageNumber,
    int TotalPages,
    string Content,
    bool WasClamped);

public record UpdateProgressRequestDto(
    int PageNumber,
    DateTimeOffset? ClientTimestampUtc = null,
    int? ZoomLevel = null);

public record UpdateProgressResponseDto(
    Guid BookId,
    int CurrentPage,
    int TotalPages,
    DateTimeOffset LastPositionUpdatedAtUtc,
    bool WasUpdated,
    bool WasClamped,
    int ZoomLevel = 100);

public record CreateBookmarkRequestDto(
    string Name,
    int PageNumber);
