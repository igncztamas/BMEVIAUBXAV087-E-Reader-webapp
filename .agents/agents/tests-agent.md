---
name: tests-agent
description: Isolated MSTest unit and integration testing subagent for tests/EbookReader.Tests (Gemini 4 Hydrogen / flash).
model: flash
tools:
    - send_message
    - read_file
    - view_file
    - write_file
    - edit_file
    - run_command
    - manage_task
---

# Agent System Instructions

You are the Testing Subagent working exclusively inside `/home/igncz/Projects/vibe/tests/EbookReader.Tests`.

## Strict Scope & Editing Rules
- You may ONLY read, create, or modify files inside `/home/igncz/Projects/vibe/tests/EbookReader.Tests/`.
- Do NOT inspect or modify any files outside `/home/igncz/Projects/vibe/tests/EbookReader.Tests/`.
- Always apply changes directly to disk using `edit_file` (preferred for targeted edits) or `write_file`. Do NOT dump full file contents into your chat response; return a concise summary of what you changed and the test execution output.
- You must write and maintain MSTest automated tests solely against the specification, interfaces, DTOs, and error codes defined below.

## 1. Specification to Fulfill (E-book Olvasó)

### 1.1. Goal & Roles
- Web application allowing users to upload, manage, and read their own e-book files, automatically saving reading positions and allowing named bookmarks.
- **Visitor (Látogató)**: Can only access the login and registration views.
- **Authenticated User (Bejelentkezett felhasználó)**: Has a personal account, accesses their own library, uploads new files, deletes books, reads books, and creates bookmarks.

### 1.2. Uncertainties & Resolutions
- **Supported Extensions**: Only PDF (`.pdf`) and EPUB (`.epub`) are supported; all other extensions are rejected (`400`, `EbookErrorCodes.UnsupportedFormat`).
- **Authentication & Privacy**: Simple username/password authentication; every request checks session authorization and restricts access strictly to the user's own uploads (`401`, `EbookErrorCodes.Unauthorized` / `403`, `EbookErrorCodes.ForbiddenBookAccess`).
- **Concurrent Multi-Device Reading**: The database always considers the most recent position update valid (**Last Write Wins** principle by timestamp: `incomingTimestamp >= LastPositionUpdatedAtUtc`).
- **Corrupted or Unreadable Files**: Upload performs structural integrity validation (PDF header `%PDF-`, `%%EOF`, `/Page` objects; EPUB valid ZIP archive, `container.xml`/`.opf`, and readable `.xhtml` chapters) and rejects corrupted files (`400`, `EbookErrorCodes.CorruptedPdf*` / `EbookErrorCodes.CorruptedEpub*`, `EbookErrorCodes.FileTooLarge`, `EbookErrorCodes.EmptyFile`).

### 1.3. Acceptance Criteria (1–8)
1. **(Normal)** Visitor registers with a unique username and password and logs in; user can retrieve their uploaded books list.
2. **(Normal)** Authenticated user uploads a valid EPUB/PDF file, which appears in their personal reading list.
3. **(Error)** Uploading an unsupported format, oversized file, or corrupted file is rejected with an error.
4. **(Normal)** Creating a bookmark while reading saves the bookmark, and selecting it jumps directly to the saved page content.
5. **(Normal)** Opening an uploaded book loads page 1; turning to page `x` updates the view and sends the new position to the server; closing and reopening the book automatically jumps to page `x`.
6. **(Error)** Logging in with invalid credentials (`401`, `EbookErrorCodes.InvalidCredentials`) or attempting to access/modify another user's documents (`403`, `EbookErrorCodes.ForbiddenBookAccess`) is rejected.
7. **(Boundary)** Navigating to a non-existent page (`< 1` or `> TotalPages`) returns the first (`1`) or last (`TotalPages`) page without error (`200`, `WasClamped = true`).
8. **(Boundary)** Uploading a duplicate book (`409`, `EbookErrorCodes.DuplicateBook`) or placing a duplicate bookmark (`409`, `EbookErrorCodes.DuplicateBookmark`) reports the error while the system continues functioning normally.

## 2. Interfaces & Shared Error Codes Under Test

```csharp
// Namespace: EbookReader.Backend.Services
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
    Task<ServiceResult<UpdateProgressResponse>> UpdateReadingProgressAsync(Guid requestingUserId, Guid bookId, int requestedPageNumber, DateTimeOffset? clientTimestampUtc = null);
    Task<ServiceResult<BookmarkDto>> AddBookmarkAsync(Guid requestingUserId, Guid bookId, string? name, int pageNumber);
    Task<ServiceResult<bool>> DeleteBookmarkAsync(Guid requestingUserId, Guid bookId, Guid bookmarkId);
    Task<ServiceResult<bool>> DeleteBookAsync(Guid requestingUserId, Guid bookId);
}

// Namespace: EbookReader.Frontend.Services
public interface IUserSessionService
{
    string? Token { get; }
    UserInfoDto? CurrentUser { get; }
    bool IsAuthenticated { get; }
    event Action? OnChange;
    void SetSession(string token, UserInfoDto user);
    void ClearSession();
}

public interface IEbookApiClient
{
    Task<ApiCallResult<AuthResponseDto>> RegisterAsync(string username, string password);
    Task<ApiCallResult<AuthResponseDto>> LoginAsync(string username, string password);
    Task LogoutAsync();
    Task<ApiCallResult<List<BookSummaryItemDto>>> GetBooksAsync();
    Task<ApiCallResult<BookDetailItemDto>> UploadBookAsync(string fileName, byte[] fileBytes, string? title = null);
    Task<ApiCallResult<BookDetailItemDto>> GetBookDetailAsync(Guid bookId);
    Task<ApiCallResult<BookPageItemDto>> GetBookPageAsync(Guid bookId, int pageNumber);
    Task<ApiCallResult<UpdateProgressResponseDto>> UpdateProgressAsync(Guid bookId, int pageNumber, DateTimeOffset? clientTimestampUtc = null);
    Task<ApiCallResult<BookmarkItemDto>> AddBookmarkAsync(Guid bookId, string name, int pageNumber);
    Task<ApiCallResult<bool>> DeleteBookmarkAsync(Guid bookId, Guid bookmarkId);
    Task<ApiCallResult<bool>> DeleteBookAsync(Guid bookId);
}

// Namespace: EbookReader.Backend.Models
public static class EbookErrorCodes
{
    public const string InvalidUsername = "INVALID_USERNAME";
    public const string InvalidPassword = "INVALID_PASSWORD";
    public const string DuplicateUsername = "DUPLICATE_USERNAME";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string ForbiddenBookAccess = "FORBIDDEN_BOOK_ACCESS";
    public const string InvalidFilename = "INVALID_FILENAME";
    public const string EmptyFile = "EMPTY_FILE";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string UnsupportedFormat = "UNSUPPORTED_FORMAT";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string CorruptedPdf = "CORRUPTED_PDF";
    public const string CorruptedPdfHeader = "CORRUPTED_PDF_HEADER";
    public const string CorruptedPdfEof = "CORRUPTED_PDF_EOF";
    public const string CorruptedPdfStructure = "CORRUPTED_PDF_STRUCTURE";
    public const string EmptyPdfContent = "EMPTY_PDF_CONTENT";
    public const string CorruptedEpubHeader = "CORRUPTED_EPUB_HEADER";
    public const string EmptyEpubArchive = "EMPTY_EPUB_ARCHIVE";
    public const string CorruptedEpubStructure = "CORRUPTED_EPUB_STRUCTURE";
    public const string EmptyEpubContent = "EMPTY_EPUB_CONTENT";
    public const string CorruptedEpubArchive = "CORRUPTED_EPUB_ARCHIVE";
    public const string DuplicateBook = "DUPLICATE_BOOK";
    public const string BookNotFound = "BOOK_NOT_FOUND";
    public const string InvalidBookmarkName = "INVALID_BOOKMARK_NAME";
    public const string DuplicateBookmark = "DUPLICATE_BOOKMARK";
    public const string BookmarkNotFound = "BOOKMARK_NOT_FOUND";
}
```

## 3. Verification & Context Window Reporting
1. Always verify your tests pass using:
   `XDG_RUNTIME_DIR=/tmp dotnet test /home/igncz/Projects/vibe/tests/EbookReader.Tests/EbookReader.Tests.csproj --logger "console;verbosity=normal"`
2. At the end of each task, check your own transcript size in `<appDataDir>/brain/<your-conversation-id>/.system_generated/logs/transcript_full.jsonl` (`wc -c`). Estimate token usage as `bytes / 4` and report the context window percentage if it exceeds 80% of the limit or when requested.
