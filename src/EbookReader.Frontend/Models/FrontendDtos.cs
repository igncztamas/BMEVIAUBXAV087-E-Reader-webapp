namespace EbookReader.Frontend.Models;

public record RegisterRequestDto(string Username, string Password);

public record LoginRequestDto(string Username, string Password);

public record UserInfoDto(Guid Id, string Username);

public record AuthResponseDto(string Token, UserInfoDto User);

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
    int BookmarkCount);

public record BookmarkItemDto(
    Guid Id,
    Guid BookId,
    string Name,
    int PageNumber,
    DateTimeOffset CreatedAtUtc);

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
    List<BookmarkItemDto> Bookmarks);

public record BookPageItemDto(
    Guid BookId,
    int RequestedPageNumber,
    int PageNumber,
    int TotalPages,
    string Content,
    bool WasClamped);

public record UpdateProgressRequestDto(
    int PageNumber,
    DateTimeOffset? ClientTimestampUtc = null);

public record UpdateProgressResponseDto(
    Guid BookId,
    int CurrentPage,
    int TotalPages,
    DateTimeOffset LastPositionUpdatedAtUtc,
    bool WasUpdated,
    bool WasClamped);

public record CreateBookmarkRequestDto(
    string Name,
    int PageNumber);

public record ApiErrorDto(
    string? Error,
    string? Code);

public class ApiCallResult<T>
{
    public bool IsSuccess { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public T? Data { get; init; }

    public static ApiCallResult<T> Success(T data, int statusCode = 200) => new()
    {
        IsSuccess = true,
        StatusCode = statusCode,
        Data = data
    };

    public static ApiCallResult<T> Failure(int statusCode, string errorMessage, string? errorCode = null) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        ErrorMessage = errorMessage,
        ErrorCode = errorCode
    };
}
