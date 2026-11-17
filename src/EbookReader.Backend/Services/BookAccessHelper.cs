namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;

public static class BookAccessHelper
{
    public static bool TryGetOwnedBook<T>(
        DatabaseSnapshot db,
        Guid bookId,
        Guid requestingUserId,
        string forbiddenMessage,
        out Book? book,
        out ServiceResult<T>? failure)
    {
        book = db.Books.FirstOrDefault(b => b.Id == bookId);
        if (book is null)
        {
            failure = ServiceResult<T>.Failure(404, EbookErrorCodes.BookNotFound, "The requested book was not found.");
            return false;
        }

        if (book.UserId != requestingUserId)
        {
            failure = ServiceResult<T>.Failure(403, EbookErrorCodes.ForbiddenBookAccess, forbiddenMessage);
            return false;
        }

        failure = null;
        return true;
    }
}
