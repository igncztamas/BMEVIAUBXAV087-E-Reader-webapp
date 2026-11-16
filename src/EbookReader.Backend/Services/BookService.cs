using EbookReader.Backend.Models;

namespace EbookReader.Backend.Services;

public class BookService : IBookService
{
    private readonly IDataStore _dataStore;
    private readonly IEbookValidatorAndParser _validator;

    public BookService(IDataStore dataStore, IEbookValidatorAndParser validator)
    {
        _dataStore = dataStore;
        _validator = validator;
    }

    public async Task<List<BookSummaryDto>> GetUserBooksAsync(Guid userId)
    {
        return await _dataStore.ReadAsync(db =>
        {
            return db.Books
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.UploadedAtUtc)
                .Select(MapToSummary)
                .ToList();
        });
    }

    public async Task<ServiceResult<BookDetailDto>> UploadBookAsync(
        Guid userId,
        string fileName,
        byte[]? fileBytes,
        string? customTitle = null)
    {
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
        var resolvedTitle = string.IsNullOrWhiteSpace(customTitle)
            ? Path.GetFileNameWithoutExtension(cleanFileName)
            : customTitle.Trim();

        return await _dataStore.WriteAsync(db =>
        {
            var duplicateBook = db.Books.FirstOrDefault(b =>
                b.UserId == userId &&
                (b.FileName.Equals(cleanFileName, StringComparison.OrdinalIgnoreCase) ||
                 b.ContentHash.Equals(parsed.ContentHash, StringComparison.OrdinalIgnoreCase)));

            if (duplicateBook is not null)
            {
                return ServiceResult<BookDetailDto>.Failure(
                    409,
                    EbookErrorCodes.DuplicateBook,
                    $"This book ('{cleanFileName}') already exists in your library.");
            }

            var now = DateTimeOffset.UtcNow;
            var book = new Book
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = resolvedTitle,
                FileName = cleanFileName,
                Format = parsed.Format,
                ContentHash = parsed.ContentHash,
                FileSizeBytes = fileBytes!.Length,
                FileBytes = fileBytes!,
                Pages = parsed.Pages,
                CurrentPage = 1,
                LastPositionUpdatedAtUtc = now,
                UploadedAtUtc = now,
                Bookmarks = new List<Bookmark>()
            };

            db.Books.Add(book);
            return ServiceResult<BookDetailDto>.Success(MapToDetail(book), 201);
        });
    }

    public async Task<ServiceResult<BookDetailDto>> GetBookDetailAsync(Guid requestingUserId, Guid bookId)
    {
        return await _dataStore.ReadAsync(db =>
        {
            var book = db.Books.FirstOrDefault(b => b.Id == bookId);
            if (book is null)
            {
                return ServiceResult<BookDetailDto>.Failure(404, EbookErrorCodes.BookNotFound, "The requested book was not found.");
            }

            if (book.UserId != requestingUserId)
            {
                return ServiceResult<BookDetailDto>.Failure(
                    403,
                    EbookErrorCodes.ForbiddenBookAccess,
                    "You do not have permission to view another user's book.");
            }

            return ServiceResult<BookDetailDto>.Success(MapToDetail(book));
        });
    }

    public async Task<ServiceResult<BookPageDto>> GetBookPageAsync(
        Guid requestingUserId,
        Guid bookId,
        int requestedPageNumber)
    {
        return await _dataStore.ReadAsync(db =>
        {
            var book = db.Books.FirstOrDefault(b => b.Id == bookId);
            if (book is null)
            {
                return ServiceResult<BookPageDto>.Failure(404, EbookErrorCodes.BookNotFound, "The requested book was not found.");
            }

            if (book.UserId != requestingUserId)
            {
                return ServiceResult<BookPageDto>.Failure(
                    403,
                    EbookErrorCodes.ForbiddenBookAccess,
                    "You do not have permission to read another user's book.");
            }

            int totalPages = Math.Max(1, book.Pages.Count);
            int clampedPage = Math.Clamp(requestedPageNumber, 1, totalPages);
            bool wasClamped = clampedPage != requestedPageNumber;
            string content = book.Pages[clampedPage - 1];

            return ServiceResult<BookPageDto>.Success(new BookPageDto(
                book.Id,
                requestedPageNumber,
                clampedPage,
                totalPages,
                content,
                wasClamped));
        });
    }

    public async Task<ServiceResult<UpdateProgressResponse>> UpdateReadingProgressAsync(
        Guid requestingUserId,
        Guid bookId,
        int requestedPageNumber,
        DateTimeOffset? clientTimestampUtc = null)
    {
        return await _dataStore.WriteAsync(db =>
        {
            var book = db.Books.FirstOrDefault(b => b.Id == bookId);
            if (book is null)
            {
                return ServiceResult<UpdateProgressResponse>.Failure(404, EbookErrorCodes.BookNotFound, "The requested book was not found.");
            }

            if (book.UserId != requestingUserId)
            {
                return ServiceResult<UpdateProgressResponse>.Failure(
                    403,
                    EbookErrorCodes.ForbiddenBookAccess,
                    "You do not have permission to update another user's reading position.");
            }

            int totalPages = Math.Max(1, book.Pages.Count);
            int clampedPage = Math.Clamp(requestedPageNumber, 1, totalPages);
            bool wasClamped = clampedPage != requestedPageNumber;
            var incomingTimestamp = clientTimestampUtc ?? DateTimeOffset.UtcNow;

            bool shouldUpdate = incomingTimestamp >= book.LastPositionUpdatedAtUtc;
            if (shouldUpdate)
            {
                book.CurrentPage = clampedPage;
                book.LastPositionUpdatedAtUtc = incomingTimestamp;
            }

            return ServiceResult<UpdateProgressResponse>.Success(new UpdateProgressResponse(
                book.Id,
                book.CurrentPage,
                totalPages,
                book.LastPositionUpdatedAtUtc,
                shouldUpdate,
                wasClamped));
        });
    }

    public async Task<ServiceResult<BookmarkDto>> AddBookmarkAsync(
        Guid requestingUserId,
        Guid bookId,
        string? name,
        int pageNumber)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return ServiceResult<BookmarkDto>.Failure(
                400,
                EbookErrorCodes.InvalidBookmarkName,
                "Bookmark name cannot be empty.");
        }

        return await _dataStore.WriteAsync(db =>
        {
            var book = db.Books.FirstOrDefault(b => b.Id == bookId);
            if (book is null)
            {
                return ServiceResult<BookmarkDto>.Failure(404, EbookErrorCodes.BookNotFound, "The requested book was not found.");
            }

            if (book.UserId != requestingUserId)
            {
                return ServiceResult<BookmarkDto>.Failure(
                    403,
                    EbookErrorCodes.ForbiddenBookAccess,
                    "You do not have permission to add a bookmark to another user's book.");
            }

            int totalPages = Math.Max(1, book.Pages.Count);
            int clampedPage = Math.Clamp(pageNumber, 1, totalPages);

            var duplicateBookmark = book.Bookmarks.FirstOrDefault(bm =>
                bm.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase) ||
                bm.PageNumber == clampedPage);

            if (duplicateBookmark is not null)
            {
                return ServiceResult<BookmarkDto>.Failure(
                    409,
                    EbookErrorCodes.DuplicateBookmark,
                    $"A bookmark already exists on this page (Page {clampedPage}) or with this name ('{trimmedName}').");
            }

            var bookmark = new Bookmark
            {
                Id = Guid.NewGuid(),
                BookId = book.Id,
                Name = trimmedName,
                PageNumber = clampedPage,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            book.Bookmarks.Add(bookmark);

            return ServiceResult<BookmarkDto>.Success(
                new BookmarkDto(bookmark.Id, bookmark.BookId, bookmark.Name, bookmark.PageNumber, bookmark.CreatedAtUtc),
                201);
        });
    }

    public async Task<ServiceResult<bool>> DeleteBookmarkAsync(
        Guid requestingUserId,
        Guid bookId,
        Guid bookmarkId)
    {
        return await _dataStore.WriteAsync(db =>
        {
            var book = db.Books.FirstOrDefault(b => b.Id == bookId);
            if (book is null)
            {
                return ServiceResult<bool>.Failure(404, EbookErrorCodes.BookNotFound, "The requested book was not found.");
            }

            if (book.UserId != requestingUserId)
            {
                return ServiceResult<bool>.Failure(
                    403,
                    EbookErrorCodes.ForbiddenBookAccess,
                    "You do not have permission to delete this bookmark.");
            }

            var removed = book.Bookmarks.RemoveAll(b => b.Id == bookmarkId);
            if (removed == 0)
            {
                return ServiceResult<bool>.Failure(404, EbookErrorCodes.BookmarkNotFound, "Bookmark not found.");
            }

            return ServiceResult<bool>.Success(true);
        });
    }

    public async Task<ServiceResult<bool>> DeleteBookAsync(Guid requestingUserId, Guid bookId)
    {
        return await _dataStore.WriteAsync(db =>
        {
            var book = db.Books.FirstOrDefault(b => b.Id == bookId);
            if (book is null)
            {
                return ServiceResult<bool>.Failure(404, EbookErrorCodes.BookNotFound, "The requested book was not found.");
            }

            if (book.UserId != requestingUserId)
            {
                return ServiceResult<bool>.Failure(
                    403,
                    EbookErrorCodes.ForbiddenBookAccess,
                    "You do not have permission to delete another user's book.");
            }

            db.Books.Remove(book);
            return ServiceResult<bool>.Success(true);
        });
    }

    public async Task<ServiceResult<(byte[] FileBytes, string Format, string FileName)>> GetBookFileAsync(
        Guid requestingUserId,
        Guid bookId)
    {
        return await _dataStore.ReadAsync(db =>
        {
            var book = db.Books.FirstOrDefault(b => b.Id == bookId);
            if (book is null)
            {
                return ServiceResult<(byte[] FileBytes, string Format, string FileName)>.Failure(
                    404,
                    EbookErrorCodes.BookNotFound,
                    "The requested book was not found.");
            }

            if (book.UserId != requestingUserId)
            {
                return ServiceResult<(byte[] FileBytes, string Format, string FileName)>.Failure(
                    403,
                    EbookErrorCodes.ForbiddenBookAccess,
                    "You do not have permission to access another user's book.");
            }

            return ServiceResult<(byte[] FileBytes, string Format, string FileName)>.Success(
                (book.FileBytes, book.Format, book.FileName));
        });
    }

    private static BookSummaryDto MapToSummary(Book book) => new(
        book.Id,
        book.UserId,
        book.Title,
        book.FileName,
        book.Format,
        book.FileSizeBytes,
        book.Pages.Count,
        book.CurrentPage,
        book.LastPositionUpdatedAtUtc,
        book.UploadedAtUtc,
        book.Bookmarks.Count);

    private static BookDetailDto MapToDetail(Book book)
    {
        int totalPages = Math.Max(1, book.Pages.Count);
        int currentPage = Math.Clamp(book.CurrentPage, 1, totalPages);
        string currentContent = book.Pages.Count > 0 ? book.Pages[currentPage - 1] : string.Empty;

        var bookmarks = book.Bookmarks
            .OrderBy(b => b.PageNumber)
            .Select(b => new BookmarkDto(b.Id, book.Id, b.Name, b.PageNumber, b.CreatedAtUtc))
            .ToList();

        return new BookDetailDto(
            book.Id,
            book.UserId,
            book.Title,
            book.FileName,
            book.Format,
            book.FileSizeBytes,
            totalPages,
            currentPage,
            currentContent,
            book.LastPositionUpdatedAtUtc,
            book.UploadedAtUtc,
            bookmarks);
    }
}
