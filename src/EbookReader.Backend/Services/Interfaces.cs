using EbookReader.Backend.Models;

namespace EbookReader.Backend.Services;

public record ParsedEbook(
    string Format,
    string ContentHash,
    List<string> Pages,
    string? MetadataTitle = null,
    string? MetadataAuthor = null,
    List<TocItem>? TableOfContents = null);

public interface IDataStore
{
    Task<TResult> ReadAsync<TResult>(Func<DatabaseSnapshot, TResult> query);
    Task<TResult> WriteAsync<TResult>(Func<DatabaseSnapshot, TResult> mutator);
}

public interface IEbookValidatorAndParser
{
    long MaxFileSizeBytes { get; set; }
    ServiceResult<ParsedEbook> ValidateAndParse(string fileName, byte[]? fileBytes);
}

public interface IAuthService
{
    Task<ServiceResult<AuthResponse>> RegisterAsync(string? username, string? password);
    Task<ServiceResult<AuthResponse>> LoginAsync(string? username, string? password);
    Task<UserDto?> ValidateTokenAsync(string? token);
    Task LogoutAsync(string? token);
}

public interface IBookService
{
    Task<List<BookSummaryDto>> GetUserBooksAsync(Guid userId);
    Task<ServiceResult<BookDetailDto>> UploadBookAsync(Guid userId, string fileName, byte[]? fileBytes, string? customTitle = null);
    Task<ServiceResult<BookDetailDto>> GetBookDetailAsync(Guid requestingUserId, Guid bookId);
    Task<ServiceResult<BookPageDto>> GetBookPageAsync(Guid requestingUserId, Guid bookId, int requestedPageNumber);
    Task<ServiceResult<UpdateProgressResponse>> UpdateReadingProgressAsync(Guid requestingUserId, Guid bookId, int requestedPageNumber, DateTimeOffset? clientTimestampUtc = null, int? zoomLevel = null);
    Task<ServiceResult<BookmarkDto>> AddBookmarkAsync(Guid requestingUserId, Guid bookId, string? name, int pageNumber);
    Task<ServiceResult<bool>> DeleteBookmarkAsync(Guid requestingUserId, Guid bookId, Guid bookmarkId);
    Task<ServiceResult<bool>> DeleteBookAsync(Guid requestingUserId, Guid bookId);
    Task<ServiceResult<(byte[] FileBytes, string Format, string FileName)>> GetBookFileAsync(Guid requestingUserId, Guid bookId);
}
