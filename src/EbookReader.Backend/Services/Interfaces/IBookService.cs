namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;

/// <summary>
/// Service interface managing user book libraries, uploads, reading progress, and bookmarks.
/// </summary>
public interface IBookService
{
    /// <summary>
    /// Retrieves summaries of all books uploaded by a specific user ordered by upload date descending.
    /// </summary>
    /// <param name="userId">The ID of the library owner.</param>
    /// <returns>A list of book summaries.</returns>
    Task<List<BookSummaryDto>> GetUserBooksAsync(Guid userId);

    /// <summary>
    /// Validates, parses, and uploads an e-book file (PDF or EPUB) to the user's library.
    /// </summary>
    /// <param name="userId">The ID of the uploading user.</param>
    /// <param name="fileName">The original filename.</param>
    /// <param name="fileBytes">The raw bytes of the file.</param>
    /// <param name="customTitle">Optional custom title override.</param>
    /// <returns>A <see cref="ServiceResult{T}"/> containing the created book detail, or validation/duplicate failure.</returns>
    Task<ServiceResult<BookDetailDto>> UploadBookAsync(Guid userId, string fileName, byte[]? fileBytes, string? customTitle = null);

    /// <summary>
    /// Retrieves detailed metadata, table of contents, and current page content for a book.
    /// </summary>
    /// <param name="requestingUserId">The ID of the user requesting the book.</param>
    /// <param name="bookId">The unique ID of the book.</param>
    /// <returns>A <see cref="ServiceResult{T}"/> containing the book details, 404 if not found, or 403 if forbidden.</returns>
    Task<ServiceResult<BookDetailDto>> GetBookDetailAsync(Guid requestingUserId, Guid bookId);

    /// <summary>
    /// Retrieves the content for a specific 1-based page of a book, clamping out-of-range requests without error.
    /// </summary>
    /// <param name="requestingUserId">The ID of the requesting user.</param>
    /// <param name="bookId">The unique ID of the book.</param>
    /// <param name="requestedPageNumber">The page number requested by the client.</param>
    /// <returns>A <see cref="ServiceResult{T}"/> containing the page DTO with clamping indicator.</returns>
    Task<ServiceResult<BookPageDto>> GetBookPageAsync(Guid requestingUserId, Guid bookId, int requestedPageNumber);

    /// <summary>
    /// Updates the current reading position and zoom level using Last-Write-Wins concurrency semantics.
    /// </summary>
    /// <param name="requestingUserId">The ID of the requesting user.</param>
    /// <param name="bookId">The unique ID of the book.</param>
    /// <param name="requestedPageNumber">The new page number.</param>
    /// <param name="clientTimestampUtc">Client timestamp for Last-Write-Wins comparison.</param>
    /// <param name="zoomLevel">Optional reader zoom percentage (50-300).</param>
    /// <returns>A <see cref="ServiceResult{T}"/> containing the progress update response.</returns>
    Task<ServiceResult<UpdateProgressResponse>> UpdateReadingProgressAsync(Guid requestingUserId, Guid bookId, int requestedPageNumber, DateTimeOffset? clientTimestampUtc = null, int? zoomLevel = null);

    /// <summary>
    /// Adds a new bookmark on a specific page of a book.
    /// </summary>
    /// <param name="requestingUserId">The ID of the requesting user.</param>
    /// <param name="bookId">The unique ID of the book.</param>
    /// <param name="name">The name/label for the bookmark.</param>
    /// <param name="pageNumber">The 1-based page number.</param>
    /// <returns>A <see cref="ServiceResult{T}"/> containing the created bookmark or duplicate failure.</returns>
    Task<ServiceResult<BookmarkDto>> AddBookmarkAsync(Guid requestingUserId, Guid bookId, string? name, int pageNumber);

    /// <summary>
    /// Deletes a bookmark from a book by bookmark ID.
    /// </summary>
    /// <param name="requestingUserId">The ID of the requesting user.</param>
    /// <param name="bookId">The unique ID of the book.</param>
    /// <param name="bookmarkId">The unique ID of the bookmark to delete.</param>
    /// <returns>A <see cref="ServiceResult{T}"/> indicating success or not found.</returns>
    Task<ServiceResult<bool>> DeleteBookmarkAsync(Guid requestingUserId, Guid bookId, Guid bookmarkId);

    /// <summary>
    /// Deletes a book and all associated bookmarks from the user's library.
    /// </summary>
    /// <param name="requestingUserId">The ID of the requesting user.</param>
    /// <param name="bookId">The unique ID of the book to delete.</param>
    /// <returns>A <see cref="ServiceResult{T}"/> indicating success, 404, or 403.</returns>
    Task<ServiceResult<bool>> DeleteBookAsync(Guid requestingUserId, Guid bookId);

    /// <summary>
    /// Retrieves the original raw file bytes, format, and filename for download.
    /// </summary>
    /// <param name="requestingUserId">The ID of the requesting user.</param>
    /// <param name="bookId">The unique ID of the book.</param>
    /// <returns>A <see cref="ServiceResult{T}"/> with file bytes tuple, 404, or 403.</returns>
    Task<ServiceResult<(byte[] FileBytes, string Format, string FileName)>> GetBookFileAsync(Guid requestingUserId, Guid bookId);
}
