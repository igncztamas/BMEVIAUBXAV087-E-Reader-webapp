namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;

/// <summary>
/// Helper class providing reusable ownership authorization and book lookup guards.
/// </summary>
public static class BookAccessHelper
{
    /// <summary>
    /// Looks up a book by ID from the database snapshot and verifies that the requesting user is its owner.
    /// </summary>
    /// <typeparam name="T">The result payload type for failure return.</typeparam>
    /// <param name="db">The database snapshot containing the books collection.</param>
    /// <param name="bookId">The unique ID of the book.</param>
    /// <param name="requestingUserId">The ID of the user requesting access.</param>
    /// <param name="forbiddenMessage">The error message to return if access is forbidden.</param>
    /// <param name="book">The retrieved book entity if successful; otherwise null.</param>
    /// <param name="failure">The failure result if lookup or ownership check fails; otherwise null.</param>
    /// <returns>True if the book was found and belongs to the user; otherwise false.</returns>
    public static bool TryGetOwnedBook<T>(
        DatabaseSnapshot db,
        Guid bookId,
        Guid requestingUserId,
        string forbiddenMessage,
        out Book? book,
        out ServiceResult<T>? failure)
    {
        // Search for the book in the database snapshot
        book = db.Books.FirstOrDefault(b => b.Id == bookId);
        if (book is null)
        {
            failure = ServiceResult<T>.Failure(404, EbookErrorCodes.BookNotFound, "The requested book was not found.");
            return false;
        }

        // Verify that the requesting user owns the book
        if (book.UserId != requestingUserId)
        {
            failure = ServiceResult<T>.Failure(403, EbookErrorCodes.ForbiddenBookAccess, forbiddenMessage);
            return false;
        }

        failure = null;
        return true;
    }
}
