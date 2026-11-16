using System.Text;
using EbookReader.Backend.Models;
using EbookReader.Backend.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EbookReader.Tests;

[TestClass]
public class AcceptanceCriteriaTests
{
    private static (IAuthService Auth, IBookService Books, IEbookValidatorAndParser Validator) CreateServices(string? filePath = null)
    {
        IDataStore store = new JsonDataStore(filePath);
        IEbookValidatorAndParser validator = new EbookValidatorAndParser();
        IAuthService auth = new AuthService(store);
        IBookService books = new BookService(store, validator);
        return (auth, books, validator);
    }

    [TestMethod]
    public async Task AC1_Normal_RegisterLoginAndListUserBooks()
    {
        var (auth, books, _) = CreateServices();

        var regResult = await auth.RegisterAsync("reader_anna", "Secret123");
        Assert.IsTrue(regResult.IsSuccess);
        Assert.AreEqual(201, regResult.StatusCode);
        Assert.IsNotNull(regResult.Data);

        var userId = regResult.Data.User.Id;
        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2");
        var upload = await books.UploadBookAsync(userId, "annas_book.pdf", pdfBytes, "Anna's Book");
        Assert.IsTrue(upload.IsSuccess);

        await auth.LogoutAsync(regResult.Data.Token);
        var loginResult = await auth.LoginAsync("reader_anna", "Secret123");
        Assert.IsTrue(loginResult.IsSuccess);
        Assert.IsNotNull(loginResult.Data);

        var validatedUser = await auth.ValidateTokenAsync(loginResult.Data.Token);
        Assert.IsNotNull(validatedUser);
        Assert.AreEqual(userId, validatedUser.Id);

        var userBooks = await books.GetUserBooksAsync(validatedUser.Id);
        Assert.AreEqual(1, userBooks.Count);
        Assert.AreEqual("Anna's Book", userBooks[0].Title);
        Assert.AreEqual("PDF", userBooks[0].Format);
    }

    [TestMethod]
    public async Task AC2_Normal_UploadValidPdfAndEpubFiles()
    {
        var (auth, books, _) = CreateServices();
        var user = (await auth.RegisterAsync("test_user", "Password123")).Data!.User;

        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("PDF Page 1", "PDF Page 2", "PDF Page 3");
        var epubBytes = TestEbookFactory.CreateValidEpubBytes("EPUB Novel", "Chapter 1 text", "Chapter 2 text");

        var pdfUpload = await books.UploadBookAsync(user.Id, "sample.pdf", pdfBytes, "Sample PDF");
        var epubUpload = await books.UploadBookAsync(user.Id, "novel.epub", epubBytes, "EPUB Novel");

        Assert.IsTrue(pdfUpload.IsSuccess);
        Assert.AreEqual(3, pdfUpload.Data!.TotalPages);
        Assert.AreEqual("PDF", pdfUpload.Data.Format);

        Assert.IsTrue(epubUpload.IsSuccess);
        Assert.AreEqual(2, epubUpload.Data!.TotalPages);
        Assert.AreEqual("EPUB", epubUpload.Data.Format);

        var list = await books.GetUserBooksAsync(user.Id);
        Assert.AreEqual(2, list.Count);
    }

    [TestMethod]
    public async Task AC3_Error_RejectInvalidFormatOversizedAndCorruptedFiles()
    {
        var (auth, books, validator) = CreateServices();
        var user = (await auth.RegisterAsync("errortest_user", "Password123")).Data!.User;

        var wrongFormat = await books.UploadBookAsync(user.Id, "document.txt", Encoding.UTF8.GetBytes("Plain text"));
        Assert.IsFalse(wrongFormat.IsSuccess);
        Assert.AreEqual(400, wrongFormat.StatusCode);
        Assert.AreEqual(EbookErrorCodes.UnsupportedFormat, wrongFormat.ErrorCode);

        var corruptedPdf = await books.UploadBookAsync(user.Id, "corrupted.pdf", Encoding.UTF8.GetBytes("%PDF-1.4 truncated data only"));
        Assert.IsFalse(corruptedPdf.IsSuccess);
        Assert.AreEqual(400, corruptedPdf.StatusCode);
        Assert.AreEqual(EbookErrorCodes.CorruptedPdfEof, corruptedPdf.ErrorCode);

        var corruptedEpub = await books.UploadBookAsync(user.Id, "corrupted.epub", Encoding.UTF8.GetBytes("PK\x03\x04invalid_zip_data"));
        Assert.IsFalse(corruptedEpub.IsSuccess);
        Assert.AreEqual(400, corruptedEpub.StatusCode);
        Assert.AreEqual(EbookErrorCodes.CorruptedEpubArchive, corruptedEpub.ErrorCode);

        validator.MaxFileSizeBytes = 1024;
        var oversizedBytes = TestEbookFactory.CreateValidPdfBytes(new string('A', 2000));
        var oversizedResult = await books.UploadBookAsync(user.Id, "too_large.pdf", oversizedBytes);
        Assert.IsFalse(oversizedResult.IsSuccess);
        Assert.AreEqual(400, oversizedResult.StatusCode);
        Assert.AreEqual(EbookErrorCodes.FileTooLarge, oversizedResult.ErrorCode);

        var userBooks = await books.GetUserBooksAsync(user.Id);
        Assert.AreEqual(0, userBooks.Count);
    }

    [TestMethod]
    public async Task AC4_Normal_CreateBookmarkAndJumpToSavedContent()
    {
        var (auth, books, _) = CreateServices();
        var user = (await auth.RegisterAsync("bookmark_user", "Password123")).Data!.User;

        var pdfBytes = TestEbookFactory.CreateValidPdfBytes(
            "Page 1: Introduction",
            "Page 2: Discussion",
            "Page 3: Key turning point in the story!",
            "Page 4: Conclusion");

        var uploaded = (await books.UploadBookAsync(user.Id, "history.pdf", pdfBytes)).Data!;

        var bmResult = await books.AddBookmarkAsync(user.Id, uploaded.Id, "Favorite part", 3);
        Assert.IsTrue(bmResult.IsSuccess);
        Assert.AreEqual(201, bmResult.StatusCode);
        Assert.AreEqual(3, bmResult.Data!.PageNumber);

        var detail = (await books.GetBookDetailAsync(user.Id, uploaded.Id)).Data!;
        Assert.AreEqual(1, detail.Bookmarks.Count);
        var savedBookmark = detail.Bookmarks[0];

        var jumpedPage = await books.GetBookPageAsync(user.Id, uploaded.Id, savedBookmark.PageNumber);
        Assert.IsTrue(jumpedPage.IsSuccess);
        Assert.AreEqual(3, jumpedPage.Data!.PageNumber);
        StringAssert.Contains(jumpedPage.Data.Content, "Key turning point");
    }

    [TestMethod]
    public async Task AC5_Normal_ReadingProgressionPersistsAcrossReopen()
    {
        var (auth, books, _) = CreateServices();
        var user = (await auth.RegisterAsync("progress_reader", "Password123")).Data!.User;

        var epubBytes = TestEbookFactory.CreateValidEpubBytes(
            "Journey",
            "Page 1 content",
            "Page 2 content",
            "Page 3 content",
            "Page 4 content");

        var book = (await books.UploadBookAsync(user.Id, "journey.epub", epubBytes)).Data!;
        Assert.AreEqual(1, book.CurrentPage);
        StringAssert.Contains(book.CurrentPageContent, "Page 1 content");

        var updateResult = await books.UpdateReadingProgressAsync(user.Id, book.Id, 3);
        Assert.IsTrue(updateResult.IsSuccess);
        Assert.AreEqual(3, updateResult.Data!.CurrentPage);

        var reopened = (await books.GetBookDetailAsync(user.Id, book.Id)).Data!;
        Assert.AreEqual(3, reopened.CurrentPage);
        StringAssert.Contains(reopened.CurrentPageContent, "Page 3 content");
    }

    [TestMethod]
    public async Task AC6_Error_InvalidLoginAndCrossUserUnauthorizedAccessRejected()
    {
        var (auth, books, _) = CreateServices();
        var alice = (await auth.RegisterAsync("alice", "AlicePass1")).Data!.User;
        var bob = (await auth.RegisterAsync("bob", "BobPass1")).Data!.User;

        var badLogin = await auth.LoginAsync("alice", "WrongPassword");
        Assert.IsFalse(badLogin.IsSuccess);
        Assert.AreEqual(401, badLogin.StatusCode);
        Assert.AreEqual(EbookErrorCodes.InvalidCredentials, badLogin.ErrorCode);

        var aliceBook = (await books.UploadBookAsync(
            alice.Id,
            "alice_secret.pdf",
            TestEbookFactory.CreateValidPdfBytes("Alice private content"))).Data!;

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

    [TestMethod]
    public async Task AC7_Boundary_NavigateToOutOfRangePageClampsToFirstOrLastPageWithoutError()
    {
        var (auth, books, _) = CreateServices();
        var user = (await auth.RegisterAsync("boundary_user", "Password123")).Data!.User;

        var pdfBytes = TestEbookFactory.CreateValidPdfBytes(
            "First page content",
            "Second page content",
            "Last (third) page content");

        var book = (await books.UploadBookAsync(user.Id, "three_pages.pdf", pdfBytes)).Data!;

        var beforeFirst = await books.GetBookPageAsync(user.Id, book.Id, -5);
        Assert.IsTrue(beforeFirst.IsSuccess);
        Assert.AreEqual(1, beforeFirst.Data!.PageNumber);
        Assert.IsTrue(beforeFirst.Data.WasClamped);
        StringAssert.Contains(beforeFirst.Data.Content, "First page content");

        var afterLast = await books.GetBookPageAsync(user.Id, book.Id, 999);
        Assert.IsTrue(afterLast.IsSuccess);
        Assert.AreEqual(3, afterLast.Data!.PageNumber);
        Assert.IsTrue(afterLast.Data.WasClamped);
        StringAssert.Contains(afterLast.Data.Content, "Last (third) page content");
    }

    [TestMethod]
    public async Task AC8_Boundary_DuplicateBookAndDuplicateBookmarkRejectedAndSystemContinuesWorking()
    {
        var (auth, books, _) = CreateServices();
        var user = (await auth.RegisterAsync("duplicate_user", "Password123")).Data!.User;

        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3");
        var firstUpload = await books.UploadBookAsync(user.Id, "book.pdf", pdfBytes);
        Assert.IsTrue(firstUpload.IsSuccess);
        var bookId = firstUpload.Data!.Id;

        var duplicateUpload = await books.UploadBookAsync(user.Id, "book.pdf", pdfBytes);
        Assert.IsFalse(duplicateUpload.IsSuccess);
        Assert.AreEqual(409, duplicateUpload.StatusCode);
        Assert.AreEqual(EbookErrorCodes.DuplicateBook, duplicateUpload.ErrorCode);

        var firstBm = await books.AddBookmarkAsync(user.Id, bookId, "Chapter 1", 1);
        Assert.IsTrue(firstBm.IsSuccess);

        var duplicateBm = await books.AddBookmarkAsync(user.Id, bookId, "Chapter 1", 2);
        Assert.IsFalse(duplicateBm.IsSuccess);
        Assert.AreEqual(409, duplicateBm.StatusCode);
        Assert.AreEqual(EbookErrorCodes.DuplicateBookmark, duplicateBm.ErrorCode);

        var secondBm = await books.AddBookmarkAsync(user.Id, bookId, "Page 3 note", 3);
        Assert.IsTrue(secondBm.IsSuccess);
        Assert.AreEqual(3, secondBm.Data!.PageNumber);
    }

    [TestMethod]
    public async Task Concurrency_LastWriteWins_IgnoresOutdatedPositionUpdates()
    {
        var (auth, books, _) = CreateServices();

        var user = (await auth.RegisterAsync("multi_device_user", "Password123")).Data!.User;
        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3", "Page 4", "Page 5");
        var book = (await books.UploadBookAsync(user.Id, "multi.pdf", pdfBytes)).Data!;

        var t1 = DateTimeOffset.UtcNow.AddMinutes(1);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(5);

        var newerUpdate = await books.UpdateReadingProgressAsync(user.Id, book.Id, 4, t2);
        Assert.IsTrue(newerUpdate.IsSuccess);
        Assert.AreEqual(4, newerUpdate.Data!.CurrentPage);

        var staleUpdate = await books.UpdateReadingProgressAsync(user.Id, book.Id, 2, t1);
        Assert.IsTrue(staleUpdate.IsSuccess);

        var detail = (await books.GetBookDetailAsync(user.Id, book.Id)).Data!;
        Assert.AreEqual(4, detail.CurrentPage, "An older timestamped (stale) position save must not overwrite a newer position!");
    }

    [TestMethod]
    public async Task Persistence_DataSurvivesStoreReload()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"ebook_test_{Guid.NewGuid():N}.json");
        try
        {
            Guid userId;
            Guid bookId;

            {
                var (auth1, books1, _) = CreateServices(tempFile);
                var reg = await auth1.RegisterAsync("persistent_user", "Password123");
                userId = reg.Data!.User.Id;

                var pdf = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3");
                var uploaded = await books1.UploadBookAsync(userId, "durable.pdf", pdf, "Durable Book");
                bookId = uploaded.Data!.Id;

                await books1.UpdateReadingProgressAsync(userId, bookId, 2);
                await books1.AddBookmarkAsync(userId, bookId, "Important page", 2);
            }

            {
                var (auth2, books2, _) = CreateServices(tempFile);
                var login = await auth2.LoginAsync("persistent_user", "Password123");
                Assert.IsTrue(login.IsSuccess);

                var detail = (await books2.GetBookDetailAsync(userId, bookId)).Data!;
                Assert.AreEqual("Durable Book", detail.Title);
                Assert.AreEqual(2, detail.CurrentPage);
                Assert.AreEqual(1, detail.Bookmarks.Count);
                Assert.AreEqual("Important page", detail.Bookmarks[0].Name);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
