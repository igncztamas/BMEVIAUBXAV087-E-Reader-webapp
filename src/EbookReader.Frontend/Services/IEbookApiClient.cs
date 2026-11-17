using EbookReader.Frontend.Models;

namespace EbookReader.Frontend.Services;

public interface IEbookApiClient
{
    Task<ApiCallResult<AuthResponseDto>> RegisterAsync(string username, string password);
    Task<ApiCallResult<AuthResponseDto>> LoginAsync(string username, string password);
    Task LogoutAsync();
    Task<ApiCallResult<List<BookSummaryItemDto>>> GetBooksAsync();
    Task<ApiCallResult<BookDetailItemDto>> UploadBookAsync(string fileName, byte[] fileBytes, string? title = null);
    Task<ApiCallResult<BookDetailItemDto>> GetBookDetailAsync(Guid bookId);
    Task<ApiCallResult<BookPageItemDto>> GetBookPageAsync(Guid bookId, int pageNumber);
    Task<ApiCallResult<UpdateProgressResponseDto>> UpdateProgressAsync(Guid bookId, int pageNumber, DateTimeOffset? clientTimestampUtc = null, int? zoomLevel = null);
    Task<ApiCallResult<BookmarkItemDto>> AddBookmarkAsync(Guid bookId, string name, int pageNumber);
    Task<ApiCallResult<bool>> DeleteBookmarkAsync(Guid bookId, Guid bookmarkId);
    Task<ApiCallResult<bool>> DeleteBookAsync(Guid bookId);
}
