using EbookReader.Backend.Models;
using EbookReader.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EbookReader.Tests;

[TestClass]
public class ReaderAndBookmarkTests
{
    [TestMethod]
    public async Task AC4_Normal_CreateBookmarkAndJumpToSavedContent()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
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
        var (auth, books, _) = TestServiceFactory.CreateServices();
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
    public async Task AC7_Boundary_NavigateToOutOfRangePageClampsToFirstOrLastPageWithoutError()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
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
        var (auth, books, _) = TestServiceFactory.CreateServices();
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
    public async Task Reader_TableOfContentsExtractedAndNavigable()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var user = (await auth.RegisterAsync("toc_user", "Password123")).Data!.User;

        var epubBytes = TestEbookFactory.CreateValidEpubBytesWithMetadata(
            "TOC Book",
            "Author A",
            ["Prologue", "Main Story", "Epilogue"],
            "Opening scene content",
            "Middle chapter content",
            "Closing scene content");

        var uploaded = (await books.UploadBookAsync(user.Id, "toc.epub", epubBytes)).Data!;
        Assert.IsNotNull(uploaded.TableOfContents);
        Assert.AreEqual(3, uploaded.TableOfContents.Count);
        Assert.AreEqual("Prologue", uploaded.TableOfContents[0].Title);
        Assert.AreEqual(1, uploaded.TableOfContents[0].PageNumber);
        Assert.AreEqual("Main Story", uploaded.TableOfContents[1].Title);
        Assert.AreEqual(2, uploaded.TableOfContents[1].PageNumber);
        Assert.AreEqual("Epilogue", uploaded.TableOfContents[2].Title);
        Assert.AreEqual(3, uploaded.TableOfContents[2].PageNumber);

        var tocJump = await books.GetBookPageAsync(user.Id, uploaded.Id, uploaded.TableOfContents[1].PageNumber);
        Assert.IsTrue(tocJump.IsSuccess);
        StringAssert.Contains(tocJump.Data!.Content, "Middle chapter content");
    }

    [TestMethod]
    public async Task Reader_ZoomLevelPersistsAndClampsAndProgressPageClamps()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var user = (await auth.RegisterAsync("zoom_user", "Password123")).Data!.User;

        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3");
        var book = (await books.UploadBookAsync(user.Id, "zoom.pdf", pdfBytes)).Data!;
        Assert.AreEqual(100, book.ZoomLevel);

        var updated = await books.UpdateReadingProgressAsync(user.Id, book.Id, 2, zoomLevel: 150);
        Assert.IsTrue(updated.IsSuccess);
        Assert.AreEqual(150, updated.Data!.ZoomLevel);
        Assert.IsFalse(updated.Data.WasClamped);

        var detail = (await books.GetBookDetailAsync(user.Id, book.Id)).Data!;
        Assert.AreEqual(150, detail.ZoomLevel);

        var summary = (await books.GetUserBooksAsync(user.Id))[0];
        Assert.AreEqual(150, summary.ZoomLevel);

        var lowZoomAndPage = await books.UpdateReadingProgressAsync(user.Id, book.Id, 0, zoomLevel: 10);
        Assert.IsTrue(lowZoomAndPage.IsSuccess);
        Assert.AreEqual(1, lowZoomAndPage.Data!.CurrentPage);
        Assert.IsTrue(lowZoomAndPage.Data.WasClamped);
        Assert.AreEqual(50, lowZoomAndPage.Data.ZoomLevel);

        var highZoomAndPage = await books.UpdateReadingProgressAsync(user.Id, book.Id, 999, zoomLevel: 500);
        Assert.IsTrue(highZoomAndPage.IsSuccess);
        Assert.AreEqual(3, highZoomAndPage.Data!.CurrentPage);
        Assert.IsTrue(highZoomAndPage.Data.WasClamped);
        Assert.AreEqual(300, highZoomAndPage.Data.ZoomLevel);
    }

    [TestMethod]
    public async Task Bookmarks_ValidationCaseInsensitiveDuplicatesAndDeletion()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var owner = (await auth.RegisterAsync("bm_owner", "Password123")).Data!.User;
        var intruder = (await auth.RegisterAsync("bm_intruder", "Password123")).Data!.User;

        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3");
        var book = (await books.UploadBookAsync(owner.Id, "bm.pdf", pdfBytes)).Data!;

        var emptyName = await books.AddBookmarkAsync(owner.Id, book.Id, "   ", 1);
        Assert.IsFalse(emptyName.IsSuccess);
        Assert.AreEqual(400, emptyName.StatusCode);
        Assert.AreEqual(EbookErrorCodes.InvalidBookmarkName, emptyName.ErrorCode);

        var first = await books.AddBookmarkAsync(owner.Id, book.Id, "Intro", 1);
        Assert.IsTrue(first.IsSuccess);

        var caseInsensitiveDup = await books.AddBookmarkAsync(owner.Id, book.Id, "intro", 2);
        Assert.IsFalse(caseInsensitiveDup.IsSuccess);
        Assert.AreEqual(409, caseInsensitiveDup.StatusCode);
        Assert.AreEqual(EbookErrorCodes.DuplicateBookmark, caseInsensitiveDup.ErrorCode);

        var samePageDup = await books.AddBookmarkAsync(owner.Id, book.Id, "Another Name", 1);
        Assert.IsFalse(samePageDup.IsSuccess);
        Assert.AreEqual(409, samePageDup.StatusCode);
        Assert.AreEqual(EbookErrorCodes.DuplicateBookmark, samePageDup.ErrorCode);

        var forbiddenDelete = await books.DeleteBookmarkAsync(intruder.Id, book.Id, first.Data!.Id);
        Assert.IsFalse(forbiddenDelete.IsSuccess);
        Assert.AreEqual(403, forbiddenDelete.StatusCode);
        Assert.AreEqual(EbookErrorCodes.ForbiddenBookAccess, forbiddenDelete.ErrorCode);

        var missingDelete = await books.DeleteBookmarkAsync(owner.Id, book.Id, Guid.NewGuid());
        Assert.IsFalse(missingDelete.IsSuccess);
        Assert.AreEqual(404, missingDelete.StatusCode);
        Assert.AreEqual(EbookErrorCodes.BookmarkNotFound, missingDelete.ErrorCode);

        var deleted = await books.DeleteBookmarkAsync(owner.Id, book.Id, first.Data.Id);
        Assert.IsTrue(deleted.IsSuccess);
        Assert.AreEqual(0, (await books.GetBookDetailAsync(owner.Id, book.Id)).Data!.Bookmarks.Count);
    }
}
