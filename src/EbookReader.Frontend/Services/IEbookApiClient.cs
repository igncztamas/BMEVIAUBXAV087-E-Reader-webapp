using EbookReader.Frontend.Models;

namespace EbookReader.Frontend.Services;

/// <summary>
/// Client interface for communicating with the backend REST API endpoints.
/// </summary>
public interface IEbookApiClient
{
    /// <summary>
    /// Registers a new user account and saves the resulting session on success.
    /// </summary>
    /// <param name="username">The requested username.</param>
    /// <param name="password">The requested password.</param>
    /// <returns>The API result containing authentication token and user information.</returns>
    Task<ApiCallResult<AuthResponseDto>> RegisterAsync(string username, string password);

    /// <summary>
    /// Authenticates an existing user and saves the resulting session on success.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <returns>The API result containing authentication token and user information.</returns>
    Task<ApiCallResult<AuthResponseDto>> LoginAsync(string username, string password);

    /// <summary>
    /// Notifies the backend of user logout and clears client session storage.
    /// </summary>
    /// <returns>A task representing the logout operation.</returns>
    Task LogoutAsync();

    /// <summary>
    /// Retrieves the list of uploaded books for the authenticated user.
    /// </summary>
    /// <returns>The API result containing a list of book summaries.</returns>
    Task<ApiCallResult<List<BookSummaryItemDto>>> GetBooksAsync();

    /// <summary>
    /// Uploads an e-book file (PDF or EPUB) to the user's library.
    /// </summary>
    /// <param name="fileName">The file name with extension.</param>
    /// <param name="fileBytes">The raw file binary bytes.</param>
    /// <param name="title">Optional custom title override.</param>
    /// <returns>The API result containing detailed information about the uploaded book.</returns>
    Task<ApiCallResult<BookDetailItemDto>> UploadBookAsync(string fileName, byte[] fileBytes, string? title = null);

    /// <summary>
    /// Retrieves book metadata, reading position, bookmarks, and TOC for the reader view.
    /// </summary>
    /// <param name="bookId">The unique book identifier.</param>
    /// <returns>The API result containing detailed book information.</returns>
    Task<ApiCallResult<BookDetailItemDto>> GetBookDetailAsync(Guid bookId);

    /// <summary>
    /// Retrieves content and navigation boundary data for a specific page of a book.
    /// </summary>
    /// <param name="bookId">The unique book identifier.</param>
    /// <param name="pageNumber">The target page number.</param>
    /// <returns>The API result containing page content and total count.</returns>
    Task<ApiCallResult<BookPageItemDto>> GetBookPageAsync(Guid bookId, int pageNumber);

    /// <summary>
    /// Updates the reading progress and zoom preference using Last-Write-Wins concurrency resolution.
    /// </summary>
    /// <param name="bookId">The unique book identifier.</param>
    /// <param name="pageNumber">The current page position.</param>
    /// <param name="clientTimestampUtc">The client-side UTC timestamp.</param>
    /// <param name="zoomLevel">The current zoom percentage.</param>
    /// <returns>The API result containing progress confirmation.</returns>
    Task<ApiCallResult<UpdateProgressResponseDto>> UpdateProgressAsync(Guid bookId, int pageNumber, DateTimeOffset? clientTimestampUtc = null, int? zoomLevel = null);

    /// <summary>
    /// Creates a bookmark on the specified page of a book.
    /// </summary>
    /// <param name="bookId">The unique book identifier.</param>
    /// <param name="name">The bookmark label.</param>
    /// <param name="pageNumber">The bookmarked page number.</param>
    /// <returns>The API result containing the created bookmark details.</returns>
    Task<ApiCallResult<BookmarkItemDto>> AddBookmarkAsync(Guid bookId, string name, int pageNumber);

    /// <summary>
    /// Deletes a specific bookmark by its identifier.
    /// </summary>
    /// <param name="bookId">The unique book identifier.</param>
    /// <param name="bookmarkId">The unique bookmark identifier.</param>
    /// <returns>The API result indicating deletion success.</returns>
    Task<ApiCallResult<bool>> DeleteBookmarkAsync(Guid bookId, Guid bookmarkId);

    /// <summary>
    /// Deletes a book and all associated bookmarks and progress from the library.
    /// </summary>
    /// <param name="bookId">The unique book identifier.</param>
    /// <returns>The API result indicating deletion success.</returns>
    Task<ApiCallResult<bool>> DeleteBookAsync(Guid bookId);
}
