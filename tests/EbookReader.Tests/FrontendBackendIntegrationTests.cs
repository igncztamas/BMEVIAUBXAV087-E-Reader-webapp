using EbookReader.Backend.Models;
using EbookReader.Backend.Services;
using EbookReader.Frontend.Services;
using EbookReader.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EbookReader.Tests;

[TestClass]
public class FrontendBackendIntegrationTests
{
    private static (IEbookApiClient Client, IUserSessionService Session) CreateClient(
        IAuthService auth,
        IBookService books)
    {
        IUserSessionService session = new TestUserSessionService();
        var httpClient = new HttpClient(new InMemoryBackendHttpMessageHandler(auth, books))
        {
            BaseAddress = new Uri("http://localhost:5050")
        };
        return (new EbookApiClient(httpClient, session), session);
    }

    [TestMethod]
    public async Task EndToEnd_FrontendClientAndBackendWorkflow()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var (apiClient, session) = CreateClient(auth, books);

        Assert.IsFalse(session.IsAuthenticated);

        var reg = await apiClient.RegisterAsync("frontend_user", "Secret1234");
        Assert.IsTrue(reg.IsSuccess);
        Assert.IsTrue(session.IsAuthenticated);
        Assert.AreEqual("frontend_user", session.CurrentUser?.Username);

        var epubBytes = TestEbookFactory.CreateValidEpubBytes(
            "Frontend Test Book",
            "Chapter 1 text",
            "Chapter 2 text",
            "Chapter 3 text");

        var upload = await apiClient.UploadBookAsync("test.epub", epubBytes, "Frontend Test Book");
        Assert.IsTrue(upload.IsSuccess);
        var bookId = upload.Data!.Id;

        var page2 = await apiClient.GetBookPageAsync(bookId, 2);
        Assert.IsTrue(page2.IsSuccess);
        StringAssert.Contains(page2.Data!.Content, "Chapter 2 text");

        var progress = await apiClient.UpdateProgressAsync(bookId, 2);
        Assert.IsTrue(progress.IsSuccess);
        Assert.AreEqual(2, progress.Data!.CurrentPage);

        var bookmark = await apiClient.AddBookmarkAsync(bookId, "Second Chapter", 2);
        Assert.IsTrue(bookmark.IsSuccess);

        await apiClient.LogoutAsync();
        Assert.IsFalse(session.IsAuthenticated);

        var login = await apiClient.LoginAsync("frontend_user", "Secret1234");
        Assert.IsTrue(login.IsSuccess);
        Assert.IsTrue(session.IsAuthenticated);

        var library = await apiClient.GetBooksAsync();
        Assert.IsTrue(library.IsSuccess);
        Assert.AreEqual(1, library.Data!.Count);
        Assert.AreEqual(2, library.Data[0].CurrentPage);
        Assert.AreEqual(1, library.Data[0].BookmarkCount);
    }

    [TestMethod]
    public async Task FrontendClient_ZoomDeleteAndErrorResponsesHandledProperly()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var (ownerClient, _) = CreateClient(auth, books);
        var (otherClient, _) = CreateClient(auth, books);

        var unauthBooks = await ownerClient.GetBooksAsync();
        Assert.IsFalse(unauthBooks.IsSuccess);
        Assert.AreEqual(401, unauthBooks.StatusCode);
        Assert.AreEqual(EbookErrorCodes.Unauthorized, unauthBooks.ErrorCode);

        Assert.IsTrue((await ownerClient.RegisterAsync("fe_owner", "Secret1234")).IsSuccess);
        var dupReg = await otherClient.RegisterAsync("fe_owner", "Secret1234");
        Assert.IsFalse(dupReg.IsSuccess);
        Assert.AreEqual(409, dupReg.StatusCode);
        Assert.AreEqual(EbookErrorCodes.DuplicateUsername, dupReg.ErrorCode);
        Assert.IsTrue((await otherClient.RegisterAsync("fe_other", "Secret1234")).IsSuccess);

        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2");
        var upload = await ownerClient.UploadBookAsync("fe.pdf", pdfBytes, "FE Book");
        Assert.IsTrue(upload.IsSuccess);
        var bookId = upload.Data!.Id;

        var dupUpload = await ownerClient.UploadBookAsync("fe.pdf", pdfBytes, "FE Book");
        Assert.IsFalse(dupUpload.IsSuccess);
        Assert.AreEqual(409, dupUpload.StatusCode);
        Assert.AreEqual(EbookErrorCodes.DuplicateBook, dupUpload.ErrorCode);

        var zoomUpdate = await ownerClient.UpdateProgressAsync(bookId, 2, zoomLevel: 140);
        Assert.IsTrue(zoomUpdate.IsSuccess);
        Assert.AreEqual(140, zoomUpdate.Data!.ZoomLevel);

        var bm = await ownerClient.AddBookmarkAsync(bookId, "Key Page", 2);
        Assert.IsTrue(bm.IsSuccess);
        Assert.IsTrue((await ownerClient.DeleteBookmarkAsync(bookId, bm.Data!.Id)).IsSuccess);

        var detail = await ownerClient.GetBookDetailAsync(bookId);
        Assert.IsTrue(detail.IsSuccess);
        Assert.AreEqual(140, detail.Data!.ZoomLevel);
        Assert.AreEqual(0, detail.Data.Bookmarks.Count);

        var forbidden = await otherClient.GetBookDetailAsync(bookId);
        Assert.IsFalse(forbidden.IsSuccess);
        Assert.AreEqual(403, forbidden.StatusCode);
        Assert.AreEqual(EbookErrorCodes.ForbiddenBookAccess, forbidden.ErrorCode);

        Assert.IsTrue((await ownerClient.DeleteBookAsync(bookId)).IsSuccess);
        var remaining = await ownerClient.GetBooksAsync();
        Assert.IsTrue(remaining.IsSuccess);
        Assert.AreEqual(0, remaining.Data!.Count);
    }
}
