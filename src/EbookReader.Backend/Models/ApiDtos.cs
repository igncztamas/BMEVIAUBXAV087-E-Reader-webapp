namespace EbookReader.Backend.Models;

public record RegisterRequest(string Username, string Password);

public record LoginRequest(string Username, string Password);

public record UserDto(Guid Id, string Username);

public record AuthResponse(string Token, UserDto User);

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
    int BookmarkCount);

public record BookmarkDto(
    Guid Id,
    Guid BookId,
    string Name,
    int PageNumber,
    DateTimeOffset CreatedAtUtc);

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
    List<BookmarkDto> Bookmarks);

public record BookPageDto(
    Guid BookId,
    int RequestedPageNumber,
    int PageNumber,
    int TotalPages,
    string Content,
    bool WasClamped);

public record UpdateProgressRequest(
    int PageNumber,
    DateTimeOffset? ClientTimestampUtc = null);

public record UpdateProgressResponse(
    Guid BookId,
    int CurrentPage,
    int TotalPages,
    DateTimeOffset LastPositionUpdatedAtUtc,
    bool WasUpdated,
    bool WasClamped);

public record CreateBookmarkRequest(
    string Name,
    int PageNumber);

public record ApiErrorResponse(
    string Error,
    string Code);

public class ServiceResult<T>
{
    public bool IsSuccess { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public T? Data { get; init; }

    public static ServiceResult<T> Success(T data, int statusCode = 200) => new()
    {
        IsSuccess = true,
        StatusCode = statusCode,
        Data = data
    };

    public static ServiceResult<T> Failure(int statusCode, string errorCode, string errorMessage) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}
