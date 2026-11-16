namespace EbookReader.Backend.Models;

/// <summary>
/// Megosztott, állandó hibakódok (ErrorCode) az E-book Olvasó üzleti szabályaihoz és HTTP válaszaihoz.
/// </summary>
public static class EbookErrorCodes
{
    // Autentikáció és jogosultság
    public const string InvalidUsername = "INVALID_USERNAME";
    public const string InvalidPassword = "INVALID_PASSWORD";
    public const string DuplicateUsername = "DUPLICATE_USERNAME";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string ForbiddenBookAccess = "FORBIDDEN_BOOK_ACCESS";

    // Fájlfeltöltés és formátum/integritás validáció
    public const string InvalidFilename = "INVALID_FILENAME";
    public const string EmptyFile = "EMPTY_FILE";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string UnsupportedFormat = "UNSUPPORTED_FORMAT";
    public const string ValidationError = "VALIDATION_ERROR";

    // PDF integritási hibák
    public const string CorruptedPdf = "CORRUPTED_PDF";
    public const string CorruptedPdfHeader = "CORRUPTED_PDF_HEADER";
    public const string CorruptedPdfEof = "CORRUPTED_PDF_EOF";
    public const string CorruptedPdfStructure = "CORRUPTED_PDF_STRUCTURE";
    public const string EmptyPdfContent = "EMPTY_PDF_CONTENT";

    // EPUB integritási hibák
    public const string CorruptedEpubHeader = "CORRUPTED_EPUB_HEADER";
    public const string EmptyEpubArchive = "EMPTY_EPUB_ARCHIVE";
    public const string CorruptedEpubStructure = "CORRUPTED_EPUB_STRUCTURE";
    public const string EmptyEpubContent = "EMPTY_EPUB_CONTENT";
    public const string CorruptedEpubArchive = "CORRUPTED_EPUB_ARCHIVE";

    // Könyvtár és könyvjelző műveletek
    public const string DuplicateBook = "DUPLICATE_BOOK";
    public const string BookNotFound = "BOOK_NOT_FOUND";
    public const string InvalidBookmarkName = "INVALID_BOOKMARK_NAME";
    public const string DuplicateBookmark = "DUPLICATE_BOOKMARK";
    public const string BookmarkNotFound = "BOOKMARK_NOT_FOUND";
}
