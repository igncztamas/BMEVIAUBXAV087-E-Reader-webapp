namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;

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
