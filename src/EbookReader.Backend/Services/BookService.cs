namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;

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
            db.Books
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.UploadedAtUtc)
                .Select(b => b.ToSummary())
                .ToList());
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

        return await _dataStore.WriteAsync(db =>
        {
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

            var book = CreateBookEntity(userId, cleanFileName, fileBytes!, customTitle, parsed);
            db.Books.Add(book);
            return ServiceResult<BookDetailDto>.Success(book.ToDetail(), 201);
        });
    }

    public async Task<ServiceResult<BookDetailDto>> GetBookDetailAsync(Guid requestingUserId, Guid bookId)
    {
        return await _dataStore.ReadAsync(db =>
        {
            if (!BookAccessHelper.TryGetOwnedBook<BookDetailDto>(
                    db, bookId, requestingUserId, "You do not have permission to view another user's book.", out var book, out var failure))
            {
                return failure!;
            }

            return ServiceResult<BookDetailDto>.Success(book!.ToDetail());
        });
    }

    public async Task<ServiceResult<BookPageDto>> GetBookPageAsync(Guid requestingUserId, Guid bookId, int requestedPageNumber)
    {
        return await _dataStore.ReadAsync(db =>
        {
            if (!BookAccessHelper.TryGetOwnedBook<BookPageDto>(
                    db, bookId, requestingUserId, "You do not have permission to read another user's book.", out var book, out var failure))
            {
                return failure!;
            }

            int totalPages = Math.Max(1, book!.Pages.Count);
            int clampedPage = Math.Clamp(requestedPageNumber, 1, totalPages);
            return ServiceResult<BookPageDto>.Success(new BookPageDto(
                book.Id, requestedPageNumber, clampedPage, totalPages, book.Pages[clampedPage - 1], clampedPage != requestedPageNumber));
        });
    }

    public async Task<ServiceResult<UpdateProgressResponse>> UpdateReadingProgressAsync(
        Guid requestingUserId, Guid bookId, int requestedPageNumber, DateTimeOffset? clientTimestampUtc = null, int? zoomLevel = null)
    {
        return await _dataStore.WriteAsync(db =>
        {
            if (!BookAccessHelper.TryGetOwnedBook<UpdateProgressResponse>(
                    db, bookId, requestingUserId, "You do not have permission to update another user's reading position.", out var book, out var failure))
            {
                return failure!;
            }

            int totalPages = Math.Max(1, book!.Pages.Count);
            int clampedPage = Math.Clamp(requestedPageNumber, 1, totalPages);
            var timestamp = clientTimestampUtc ?? DateTimeOffset.UtcNow;

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

    public async Task<ServiceResult<BookmarkDto>> AddBookmarkAsync(Guid requestingUserId, Guid bookId, string? name, int pageNumber)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return ServiceResult<BookmarkDto>.Failure(400, EbookErrorCodes.InvalidBookmarkName, "Bookmark name cannot be empty.");
        }

        return await _dataStore.WriteAsync(db =>
        {
            if (!BookAccessHelper.TryGetOwnedBook<BookmarkDto>(
                    db, bookId, requestingUserId, "You do not have permission to add a bookmark to another user's book.", out var book, out var failure))
            {
                return failure!;
            }

            int totalPages = Math.Max(1, book!.Pages.Count);
            int clampedPage = Math.Clamp(pageNumber, 1, totalPages);

            var duplicate = book.Bookmarks.FirstOrDefault(bm =>
                bm.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase) || bm.PageNumber == clampedPage);

            if (duplicate is not null)
            {
                return ServiceResult<BookmarkDto>.Failure(
                    409, EbookErrorCodes.DuplicateBookmark, $"A bookmark already exists on this page (Page {clampedPage}) or with this name ('{trimmedName}').");
            }

            var bookmark = new Bookmark { Id = Guid.NewGuid(), BookId = book.Id, Name = trimmedName, PageNumber = clampedPage, CreatedAtUtc = DateTimeOffset.UtcNow };
            book.Bookmarks.Add(bookmark);

            return ServiceResult<BookmarkDto>.Success(new BookmarkDto(bookmark.Id, bookmark.BookId, bookmark.Name, bookmark.PageNumber, bookmark.CreatedAtUtc), 201);
        });
    }

    public async Task<ServiceResult<bool>> DeleteBookmarkAsync(Guid requestingUserId, Guid bookId, Guid bookmarkId)
    {
        return await _dataStore.WriteAsync(db =>
        {
            if (!BookAccessHelper.TryGetOwnedBook<bool>(
                    db, bookId, requestingUserId, "You do not have permission to delete this bookmark.", out var book, out var failure))
            {
                return failure!;
            }

            var removed = book!.Bookmarks.RemoveAll(b => b.Id == bookmarkId);
            return removed == 0
                ? ServiceResult<bool>.Failure(404, EbookErrorCodes.BookmarkNotFound, "Bookmark not found.")
                : ServiceResult<bool>.Success(true);
        });
    }

    public async Task<ServiceResult<bool>> DeleteBookAsync(Guid requestingUserId, Guid bookId)
    {
        return await _dataStore.WriteAsync(db =>
        {
            if (!BookAccessHelper.TryGetOwnedBook<bool>(
                    db, bookId, requestingUserId, "You do not have permission to delete another user's book.", out var book, out var failure))
            {
                return failure!;
            }

            db.Books.Remove(book!);
            return ServiceResult<bool>.Success(true);
        });
    }

    public async Task<ServiceResult<(byte[] FileBytes, string Format, string FileName)>> GetBookFileAsync(Guid requestingUserId, Guid bookId)
    {
        return await _dataStore.ReadAsync(db =>
        {
            if (!BookAccessHelper.TryGetOwnedBook<(byte[] FileBytes, string Format, string FileName)>(
                    db, bookId, requestingUserId, "You do not have permission to access another user's book.", out var book, out var failure))
            {
                return failure!;
            }

            return ServiceResult<(byte[] FileBytes, string Format, string FileName)>.Success((book!.FileBytes, book.Format, book.FileName));
        });
    }

    private static Book CreateBookEntity(Guid userId, string cleanFileName, byte[] fileBytes, string? customTitle, ParsedEbook parsed)
    {
        var resolvedTitle = !string.IsNullOrWhiteSpace(customTitle)
            ? customTitle.Trim()
            : (!string.IsNullOrWhiteSpace(parsed.MetadataTitle)
                ? parsed.MetadataTitle.Trim()
                : Path.GetFileNameWithoutExtension(cleanFileName));

        var resolvedAuthor = !string.IsNullOrWhiteSpace(parsed.MetadataAuthor)
            ? parsed.MetadataAuthor.Trim()
            : cleanFileName;

        var now = DateTimeOffset.UtcNow;
        return new Book
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = resolvedTitle,
            Author = resolvedAuthor,
            FileName = cleanFileName,
            Format = parsed.Format,
            ContentHash = parsed.ContentHash,
            FileSizeBytes = fileBytes.Length,
            FileBytes = fileBytes,
            Pages = parsed.Pages,
            CurrentPage = 1,
            ZoomLevel = 100,
            LastPositionUpdatedAtUtc = now,
            UploadedAtUtc = now,
            Bookmarks = new List<Bookmark>(),
            TableOfContents = parsed.TableOfContents ?? new List<TocItem>()
        };
    }
}
