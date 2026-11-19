using System.Text;
using EbookReader.Backend.Models;
using EbookReader.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EbookReader.Tests;

/// <summary>
/// Verifies authentication, session token lifecycle, library upload/download/delete, and cross-user authorization (AC1, AC2, AC3, AC6).
/// </summary>
[TestClass]
public class AuthAndLibraryTests
{
    /// <summary>
    /// AC1 (Normal): Registers a user, uploads a book, logs out and back in, validates the token, and lists personal books.
    /// </summary>
    [TestMethod]
    public async Task AC1_Normal_RegisterLoginAndListUserBooks()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();

        // Register a new user and verify 201 Created response with session data.
        var regResult = await auth.RegisterAsync("reader_anna", "Secret123");
        Assert.IsTrue(regResult.IsSuccess);
        Assert.AreEqual(201, regResult.StatusCode);
        Assert.IsNotNull(regResult.Data);

        // Upload a valid PDF into the user's library.
        var userId = regResult.Data.User.Id;
        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2");
        var upload = await books.UploadBookAsync(userId, "annas_book.pdf", pdfBytes, "Anna's Book");
        Assert.IsTrue(upload.IsSuccess);

        // Log out, then log back in and validate the newly issued session token.
        await auth.LogoutAsync(regResult.Data.Token);
        var loginResult = await auth.LoginAsync("reader_anna", "Secret123");
        Assert.IsTrue(loginResult.IsSuccess);
        Assert.IsNotNull(loginResult.Data);

        var validatedUser = await auth.ValidateTokenAsync(loginResult.Data.Token);
        Assert.IsNotNull(validatedUser);
        Assert.AreEqual(userId, validatedUser.Id);

        // Verify the uploaded book appears in the authenticated user's reading list.
        var userBooks = await books.GetUserBooksAsync(validatedUser.Id);
        Assert.AreEqual(1, userBooks.Count);
        Assert.AreEqual("Anna's Book", userBooks[0].Title);
        Assert.AreEqual("PDF", userBooks[0].Format);
    }

    /// <summary>
    /// AC2 (Normal): Uploads both valid PDF and EPUB files and verifies page counts, formats, and library listing.
    /// </summary>
    [TestMethod]
    public async Task AC2_Normal_UploadValidPdfAndEpubFiles()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var user = (await auth.RegisterAsync("test_user", "Password123")).Data!.User;

        // Build standard multi-page PDF and multi-chapter EPUB payloads.
        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("PDF Page 1", "PDF Page 2", "PDF Page 3");
        var epubBytes = TestEbookFactory.CreateValidEpubBytes("EPUB Novel", "Chapter 1 text", "Chapter 2 text");

        // Upload both files into the user's library.
        var pdfUpload = await books.UploadBookAsync(user.Id, "sample.pdf", pdfBytes, "Sample PDF");
        var epubUpload = await books.UploadBookAsync(user.Id, "novel.epub", epubBytes, "EPUB Novel");

        // Assert parsed page counts, formats, and total library count.
        Assert.IsTrue(pdfUpload.IsSuccess);
        Assert.AreEqual(3, pdfUpload.Data!.TotalPages);
        Assert.AreEqual("PDF", pdfUpload.Data.Format);

        Assert.IsTrue(epubUpload.IsSuccess);
        Assert.AreEqual(2, epubUpload.Data!.TotalPages);
        Assert.AreEqual("EPUB", epubUpload.Data.Format);

        var list = await books.GetUserBooksAsync(user.Id);
        Assert.AreEqual(2, list.Count);
    }

    /// <summary>
    /// AC3 (Error): Rejects unsupported extensions, corrupted PDFs/EPUBs, and oversized files without storing any book.
    /// </summary>
    [TestMethod]
    public async Task AC3_Error_RejectInvalidFormatOversizedAndCorruptedFiles()
    {
        // Configure a 1 KB limit so oversized upload rejection can be tested cleanly.
        var (auth, books, _) = TestServiceFactory.CreateServices(maxFileSizeBytes: 1024);
        var user = (await auth.RegisterAsync("errortest_user", "Password123")).Data!.User;

        // Unsupported file extension (.txt) must return 400 UnsupportedFormat.
        var wrongFormat = await books.UploadBookAsync(user.Id, "document.txt", Encoding.UTF8.GetBytes("Plain text"));
        Assert.IsFalse(wrongFormat.IsSuccess);
        Assert.AreEqual(400, wrongFormat.StatusCode);
        Assert.AreEqual(EbookErrorCodes.UnsupportedFormat, wrongFormat.ErrorCode);

        // Truncated PDF missing %%EOF must return 400 CorruptedPdfEof.
        var corruptedPdf = await books.UploadBookAsync(user.Id, "corrupted.pdf", Encoding.UTF8.GetBytes("%PDF-1.4 truncated data only"));
        Assert.IsFalse(corruptedPdf.IsSuccess);
        Assert.AreEqual(400, corruptedPdf.StatusCode);
        Assert.AreEqual(EbookErrorCodes.CorruptedPdfEof, corruptedPdf.ErrorCode);

        // Invalid ZIP/EPUB payload must return 400 CorruptedEpubArchive or CorruptedEpubStructure.
        var corruptedEpub = await books.UploadBookAsync(user.Id, "corrupted.epub", Encoding.UTF8.GetBytes("PK\x03\x04invalid_zip_data"));
        Assert.IsFalse(corruptedEpub.IsSuccess);
        Assert.AreEqual(400, corruptedEpub.StatusCode);
        Assert.IsTrue(corruptedEpub.ErrorCode is EbookErrorCodes.CorruptedEpubArchive or EbookErrorCodes.CorruptedEpubStructure);

        // File exceeding the configured maximum byte limit must return 400 FileTooLarge.
        var oversizedBytes = TestEbookFactory.CreateValidPdfBytes(new string('A', 2000));
        var oversizedResult = await books.UploadBookAsync(user.Id, "too_large.pdf", oversizedBytes);
        Assert.IsFalse(oversizedResult.IsSuccess);
        Assert.AreEqual(400, oversizedResult.StatusCode);
        Assert.AreEqual(EbookErrorCodes.FileTooLarge, oversizedResult.ErrorCode);

        // Ensure no rejected files were persisted in the user's library.
        var userBooks = await books.GetUserBooksAsync(user.Id);
        Assert.AreEqual(0, userBooks.Count);
    }

    /// <summary>
    /// AC6 (Error): Rejects invalid login credentials (401) and cross-user read/update/delete operations (403).
    /// </summary>
    [TestMethod]
    public async Task AC6_Error_InvalidLoginAndCrossUserUnauthorizedAccessRejected()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var alice = (await auth.RegisterAsync("alice", "AlicePass1")).Data!.User;
        var bob = (await auth.RegisterAsync("bob", "BobPass1")).Data!.User;

        // Invalid password login must return 401 InvalidCredentials.
        var badLogin = await auth.LoginAsync("alice", "WrongPassword");
        Assert.IsFalse(badLogin.IsSuccess);
        Assert.AreEqual(401, badLogin.StatusCode);
        Assert.AreEqual(EbookErrorCodes.InvalidCredentials, badLogin.ErrorCode);

        // Alice uploads a private book that Bob must not be able to inspect or modify.
        var aliceBook = (await books.UploadBookAsync(
            alice.Id,
            "alice_secret.pdf",
            TestEbookFactory.CreateValidPdfBytes("Alice private content"))).Data!;

        // Cross-user detail, page read, progress update, and deletion must all return 403 ForbiddenBookAccess.
        var bobReadDetail = await books.GetBookDetailAsync(bob.Id, aliceBook.Id);
        Assert.IsFalse(bobReadDetail.IsSuccess);
        Assert.AreEqual(403, bobReadDetail.StatusCode);
        Assert.AreEqual(EbookErrorCodes.ForbiddenBookAccess, bobReadDetail.ErrorCode);

        var bobReadPage = await books.GetBookPageAsync(bob.Id, aliceBook.Id, 1);
        Assert.IsFalse(bobReadPage.IsSuccess);
        Assert.AreEqual(403, bobReadPage.StatusCode);
        Assert.AreEqual(EbookErrorCodes.ForbiddenBookAccess, bobReadPage.ErrorCode);

        var bobUpdateProgress = await books.UpdateReadingProgressAsync(bob.Id, aliceBook.Id, 1);
        Assert.IsFalse(bobUpdateProgress.IsSuccess);
        Assert.AreEqual(403, bobUpdateProgress.StatusCode);
        Assert.AreEqual(EbookErrorCodes.ForbiddenBookAccess, bobUpdateProgress.ErrorCode);

        var bobDelete = await books.DeleteBookAsync(bob.Id, aliceBook.Id);
        Assert.IsFalse(bobDelete.IsSuccess);
        Assert.AreEqual(403, bobDelete.StatusCode);
        Assert.AreEqual(EbookErrorCodes.ForbiddenBookAccess, bobDelete.ErrorCode);
    }

    /// <summary>
    /// Verifies registration input validation (short username/password), duplicate username rejection (409),
    /// invalid/empty token handling, and token invalidation after logout.
    /// </summary>
    [TestMethod]
    public async Task Auth_ValidationErrorsAndLogout_HandledCorrectly()
    {
        var (auth, _, _) = TestServiceFactory.CreateServices();

        // Short username and short password must return 400 validation errors.
        var shortUsername = await auth.RegisterAsync("ab", "Password123");
        Assert.IsFalse(shortUsername.IsSuccess);
        Assert.AreEqual(400, shortUsername.StatusCode);
        Assert.AreEqual(EbookErrorCodes.InvalidUsername, shortUsername.ErrorCode);

        var shortPassword = await auth.RegisterAsync("valid_user", "123");
        Assert.IsFalse(shortPassword.IsSuccess);
        Assert.AreEqual(400, shortPassword.StatusCode);
        Assert.AreEqual(EbookErrorCodes.InvalidPassword, shortPassword.ErrorCode);

        // Duplicate username registration must return 409 DuplicateUsername.
        var firstReg = await auth.RegisterAsync("unique_user", "Password123");
        Assert.IsTrue(firstReg.IsSuccess);

        var dupReg = await auth.RegisterAsync("unique_user", "AnotherPass1");
        Assert.IsFalse(dupReg.IsSuccess);
        Assert.AreEqual(409, dupReg.StatusCode);
        Assert.AreEqual(EbookErrorCodes.DuplicateUsername, dupReg.ErrorCode);

        // Null, whitespace, and unknown tokens must fail validation cleanly.
        Assert.IsNull(await auth.ValidateTokenAsync(null));
        Assert.IsNull(await auth.ValidateTokenAsync("   "));
        Assert.IsNull(await auth.ValidateTokenAsync("nonexistent-token"));

        // Logging out must immediately invalidate the previously issued token.
        await auth.LogoutAsync(firstReg.Data!.Token);
        Assert.IsNull(await auth.ValidateTokenAsync(firstReg.Data.Token));
    }

    /// <summary>
    /// Verifies <c>GetBookFileAsync</c> raw file retrieval, cross-user/missing-book guards (403/404),
    /// and <c>DeleteBookAsync</c> cleanup returning 404 on subsequent reads or deletes.
    /// </summary>
    [TestMethod]
    public async Task Library_GetBookFileAndDeleteBookLifecycle()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var owner = (await auth.RegisterAsync("file_owner", "Password123")).Data!.User;
        var intruder = (await auth.RegisterAsync("file_intruder", "Password123")).Data!.User;

        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2");
        var uploaded = (await books.UploadBookAsync(owner.Id, "downloadable.pdf", pdfBytes)).Data!;

        // Owner retrieves exact uploaded bytes, format, and original file name.
        var fileResult = await books.GetBookFileAsync(owner.Id, uploaded.Id);
        Assert.IsTrue(fileResult.IsSuccess);
        CollectionAssert.AreEqual(pdfBytes, fileResult.Data.FileBytes);
        Assert.AreEqual("PDF", fileResult.Data.Format);
        Assert.AreEqual("downloadable.pdf", fileResult.Data.FileName);

        // Another user or missing book ID must be rejected with 403 or 404 respectively.
        var forbiddenFile = await books.GetBookFileAsync(intruder.Id, uploaded.Id);
        Assert.IsFalse(forbiddenFile.IsSuccess);
        Assert.AreEqual(403, forbiddenFile.StatusCode);
        Assert.AreEqual(EbookErrorCodes.ForbiddenBookAccess, forbiddenFile.ErrorCode);

        var missingFile = await books.GetBookFileAsync(owner.Id, Guid.NewGuid());
        Assert.IsFalse(missingFile.IsSuccess);
        Assert.AreEqual(404, missingFile.StatusCode);
        Assert.AreEqual(EbookErrorCodes.BookNotFound, missingFile.ErrorCode);

        // Deleting the book removes it from the library and causes subsequent lookups/deletes to return 404.
        var deleted = await books.DeleteBookAsync(owner.Id, uploaded.Id);
        Assert.IsTrue(deleted.IsSuccess);
        Assert.IsTrue(deleted.Data);
        Assert.AreEqual(0, (await books.GetUserBooksAsync(owner.Id)).Count);

        var detailAfterDelete = await books.GetBookDetailAsync(owner.Id, uploaded.Id);
        Assert.IsFalse(detailAfterDelete.IsSuccess);
        Assert.AreEqual(404, detailAfterDelete.StatusCode);
        Assert.AreEqual(EbookErrorCodes.BookNotFound, detailAfterDelete.ErrorCode);

        var deleteAgain = await books.DeleteBookAsync(owner.Id, uploaded.Id);
        Assert.IsFalse(deleteAgain.IsSuccess);
        Assert.AreEqual(404, deleteAgain.StatusCode);
        Assert.AreEqual(EbookErrorCodes.BookNotFound, deleteAgain.ErrorCode);
    }
}
