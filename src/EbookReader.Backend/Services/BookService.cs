namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;

/// <summary>
/// Service managing user library books, file uploads, reading progress tracking, and bookmarks.
/// </summary>
public class BookService : IBookService
{
    private readonly IDataStore _dataStore;
    private readonly IEbookValidatorAndParser _validator;

    /// <summary>
    /// Initializes a new instance of <see cref="BookService"/>.
    /// </summary>
    /// <param name="dataStore">Data store for persisting books and progress.</param>
    /// <param name="validator">Validator and parser for PDF and EPUB files.</param>
    public BookService(IDataStore dataStore, IEbookValidatorAndParser validator)
    {
        _dataStore = dataStore;
        _validator = validator;
    }

    /// <inheritdoc/>
    public async Task<List<BookSummaryDto>> GetUserBooksAsync(Guid userId)
    {
        return await _dataStore.ReadAsync(db =>
            db.Books
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.UploadedAtUtc)
                .Select(b => b.ToSummary())
                .ToList());
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<BookDetailDto>> UploadBookAsync(
        Guid userId,
        string fileName,
        byte[]? fileBytes,
        string? customTitle = null)
    {
        // Validate file integrity, size, and parse pages/metadata
        var parseResult = _validator.ValidateAndParse(fileName, fileBytes);
        if (!parseResult.IsSuccess || parseResult.Data is null)
        {
            return ServiceResult<BookDetailDto>.Failure(
                parseResult.StatusCode,
                parseResult.ErrorCode ?? EbookErrorCodes.ValidationError,
                parseResult.ErrorMessage ?? "Invalid file.");
        }

        var parsed = parseResult.Data;
        var cleanFileName = Path.GetFileName(fileName.Trim());

        return await _dataStore.WriteAsync(db =>
        {
            // Duplicate detection: check filename or SHA-256 content hash in user's library
            var duplicate = db.Books.FirstOrDefault(b =>
                b.UserId == userId &&
                (b.FileName.Equals(cleanFileName, StringComparison.OrdinalIgnoreCase) ||
                 b.ContentHash.Equals(parsed.ContentHash, StringComparison.OrdinalIgnoreCase)));

            if (duplicate is not null)
            {
                return ServiceResult<BookDetailDto>.Failure(
                    409,
                    EbookErrorCodes.DuplicateBook,
                    $"This book ('{cleanFileName}') already exists in your library.");
            }

            // Create domain entity and persist to library
            var book = BookMapper.CreateBookEntity(userId, cleanFileName, fileBytes!, customTitle, parsed);
            db.Books.Add(book);
            return ServiceResult<BookDetailDto>.Success(book.ToDetail(), 201);
        });
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<BookDetailDto>> GetBookDetailAsync(Guid requestingUserId, Guid bookId)
    {
        return await _dataStore.ReadAsync(db =>
        {
            // Verify existence and ownership authorization
            if (!BookAccessHelper.TryGetOwnedBook<BookDetailDto>(
                    db, bookId, requestingUserId, "You do not have permission to view another user's book.", out var book, out var failure))
            {
                return failure!;
            }

            return ServiceResult<BookDetailDto>.Success(book!.ToDetail());
        });
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<BookPageDto>> GetBookPageAsync(Guid requestingUserId, Guid bookId, int requestedPageNumber)
    {
        return await _dataStore.ReadAsync(db =>
        {
            // Verify existence and ownership authorization
            if (!BookAccessHelper.TryGetOwnedBook<BookPageDto>(
                    db, bookId, requestingUserId, "You do not have permission to read another user's book.", out var book, out var failure))
            {
                return failure!;
            }

            // Ensure on-demand page content is loaded
            book!.EnsureContentLoaded();

            // Clamp requested page to valid range [1, TotalPages] without throwing an error
            int totalPages = Math.Max(1, book.TotalPages > 0 ? book.TotalPages : book.Pages.Count);
            int clampedPage = Math.Clamp(requestedPageNumber, 1, totalPages);
            string content = (book.Pages.Count > 0 && clampedPage <= book.Pages.Count) ? book.Pages[clampedPage - 1] : string.Empty;
            return ServiceResult<BookPageDto>.Success(new BookPageDto(
                book.Id, requestedPageNumber, clampedPage, totalPages, content, clampedPage != requestedPageNumber));
        });
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<UpdateProgressResponse>> UpdateReadingProgressAsync(
        Guid requestingUserId, Guid bookId, int requestedPageNumber, DateTimeOffset? clientTimestampUtc = null, int? zoomLevel = null)
    {
        return await _dataStore.WriteAsync(db =>
        {
            // Verify existence and ownership authorization
            if (!BookAccessHelper.TryGetOwnedBook<UpdateProgressResponse>(
                    db, bookId, requestingUserId, "You do not have permission to update another user's reading position.", out var book, out var failure))
            {
                return failure!;
            }

            // Clamp page to valid bounds
            int totalPages = Math.Max(1, book!.TotalPages > 0 ? book.TotalPages : book.Pages.Count);
            int clampedPage = Math.Clamp(requestedPageNumber, 1, totalPages);
            var timestamp = clientTimestampUtc ?? DateTimeOffset.UtcNow;

            // Last-Write-Wins concurrency: only apply update if client timestamp is >= stored timestamp
            bool shouldUpdate = timestamp >= book.LastPositionUpdatedAtUtc;
            if (shouldUpdate)
            {
                book.CurrentPage = clampedPage;
                book.LastPositionUpdatedAtUtc = timestamp;
                if (zoomLevel.HasValue)
                {
                    book.ZoomLevel = Math.Clamp(zoomLevel.Value, 50, 300);
                }
            }

            return ServiceResult<UpdateProgressResponse>.Success(new UpdateProgressResponse(
                book.Id, book.CurrentPage, totalPages, book.LastPositionUpdatedAtUtc, shouldUpdate, clampedPage != requestedPageNumber, book.ZoomLevel <= 0 ? 100 : book.ZoomLevel));
        });
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<BookmarkDto>> AddBookmarkAsync(Guid requestingUserId, Guid bookId, string? name, int pageNumber)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return ServiceResult<BookmarkDto>.Failure(400, EbookErrorCodes.InvalidBookmarkName, "Bookmark name cannot be empty.");
        }

        return await _dataStore.WriteAsync(db =>
        {
            // Verify existence and ownership authorization
            if (!BookAccessHelper.TryGetOwnedBook<BookmarkDto>(
                    db, bookId, requestingUserId, "You do not have permission to add a bookmark to another user's book.", out var book, out var failure))
            {
                return failure!;
            }

            // Clamp target page
            int totalPages = Math.Max(1, book!.TotalPages > 0 ? book.TotalPages : book.Pages.Count);
            int clampedPage = Math.Clamp(pageNumber, 1, totalPages);

            // Duplicate detection: prevent bookmarks on same page or with same name in this book
            var duplicate = book.Bookmarks.FirstOrDefault(bm =>
                bm.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase) || bm.PageNumber == clampedPage);

            if (duplicate is not null)
            {
                return ServiceResult<BookmarkDto>.Failure(
                    409, EbookErrorCodes.DuplicateBookmark, $"A bookmark already exists on this page (Page {clampedPage}) or with this name ('{trimmedName}').");
            }

            // Create bookmark entity
            var bookmark = new Bookmark { Id = Guid.NewGuid(), BookId = book.Id, Name = trimmedName, PageNumber = clampedPage, CreatedAtUtc = DateTimeOffset.UtcNow };
            book.Bookmarks.Add(bookmark);

            return ServiceResult<BookmarkDto>.Success(new BookmarkDto(bookmark.Id, bookmark.BookId, bookmark.Name, bookmark.PageNumber, bookmark.CreatedAtUtc), 201);
        });
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<bool>> DeleteBookmarkAsync(Guid requestingUserId, Guid bookId, Guid bookmarkId)
    {
        return await _dataStore.WriteAsync(db =>
        {
            // Verify existence and ownership authorization
            if (!BookAccessHelper.TryGetOwnedBook<bool>(
                    db, bookId, requestingUserId, "You do not have permission to delete this bookmark.", out var book, out var failure))
            {
                return failure!;
            }

            // Remove bookmark matching ID
            var removed = book!.Bookmarks.RemoveAll(b => b.Id == bookmarkId);
            return removed == 0
                ? ServiceResult<bool>.Failure(404, EbookErrorCodes.BookmarkNotFound, "Bookmark not found.")
                : ServiceResult<bool>.Success(true);
        });
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<bool>> DeleteBookAsync(Guid requestingUserId, Guid bookId)
    {
        return await _dataStore.WriteAsync(db =>
        {
            // Verify existence and ownership authorization
            if (!BookAccessHelper.TryGetOwnedBook<bool>(
                    db, bookId, requestingUserId, "You do not have permission to delete another user's book.", out var book, out var failure))
            {
                return failure!;
            }

            db.Books.Remove(book!);
            return ServiceResult<bool>.Success(true);
        });
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<(byte[] FileBytes, string Format, string FileName)>> GetBookFileAsync(Guid requestingUserId, Guid bookId)
    {
        return await _dataStore.ReadAsync(db =>
        {
            // Verify existence and ownership authorization
            if (!BookAccessHelper.TryGetOwnedBook<(byte[] FileBytes, string Format, string FileName)>(
                    db, bookId, requestingUserId, "You do not have permission to access another user's book.", out var book, out var failure))
            {
                return failure!;
            }

            // Ensure full file bytes are loaded into memory
            book!.EnsureContentLoaded();

            return ServiceResult<(byte[] FileBytes, string Format, string FileName)>.Success((book.FileBytes, book.Format, book.FileName));
        });
    }
}
