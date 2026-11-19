using EbookReader.Backend.Models;
using EbookReader.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EbookReader.Tests;

/// <summary>
/// Verifies reading progression, TOC navigation, zoom persistence, page clamping, and bookmark management (AC4, AC5, AC7, AC8).
/// </summary>
[TestClass]
public class ReaderAndBookmarkTests
{
    /// <summary>
    /// AC4 (Normal): Creates a bookmark at page 3, verifies it in book details, and jumps directly to the saved page content.
    /// </summary>
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

        // Create a named bookmark pointing to page 3.
        var bmResult = await books.AddBookmarkAsync(user.Id, uploaded.Id, "Favorite part", 3);
        Assert.IsTrue(bmResult.IsSuccess);
        Assert.AreEqual(201, bmResult.StatusCode);
        Assert.AreEqual(3, bmResult.Data!.PageNumber);

        // Retrieve saved bookmark from book detail and jump to its page.
        var detail = (await books.GetBookDetailAsync(user.Id, uploaded.Id)).Data!;
        Assert.AreEqual(1, detail.Bookmarks.Count);
        var savedBookmark = detail.Bookmarks[0];

        var jumpedPage = await books.GetBookPageAsync(user.Id, uploaded.Id, savedBookmark.PageNumber);
        Assert.IsTrue(jumpedPage.IsSuccess);
        Assert.AreEqual(3, jumpedPage.Data!.PageNumber);
        StringAssert.Contains(jumpedPage.Data.Content, "Key turning point");
    }

    /// <summary>
    /// AC5 (Normal): Verifies a newly uploaded book opens at page 1, saves progress to page 3, and reopens at page 3.
    /// </summary>
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

        // Newly uploaded book must start at page 1.
        var book = (await books.UploadBookAsync(user.Id, "journey.epub", epubBytes)).Data!;
        Assert.AreEqual(1, book.CurrentPage);
        StringAssert.Contains(book.CurrentPageContent, "Page 1 content");

        // Turn to page 3 and verify the saved position persists when reopening the book.
        var updateResult = await books.UpdateReadingProgressAsync(user.Id, book.Id, 3);
        Assert.IsTrue(updateResult.IsSuccess);
        Assert.AreEqual(3, updateResult.Data!.CurrentPage);

        var reopened = (await books.GetBookDetailAsync(user.Id, book.Id)).Data!;
        Assert.AreEqual(3, reopened.CurrentPage);
        StringAssert.Contains(reopened.CurrentPageContent, "Page 3 content");
    }

    /// <summary>
    /// AC7 (Boundary): Navigating below page 1 or past <c>TotalPages</c> clamps to the first or last page with <c>WasClamped = true</c>.
    /// </summary>
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

        // Negative page request must clamp to page 1 without error.
        var beforeFirst = await books.GetBookPageAsync(user.Id, book.Id, -5);
        Assert.IsTrue(beforeFirst.IsSuccess);
        Assert.AreEqual(1, beforeFirst.Data!.PageNumber);
        Assert.IsTrue(beforeFirst.Data.WasClamped);
        StringAssert.Contains(beforeFirst.Data.Content, "First page content");

        // Page beyond TotalPages must clamp to the last page without error.
        var afterLast = await books.GetBookPageAsync(user.Id, book.Id, 999);
        Assert.IsTrue(afterLast.IsSuccess);
        Assert.AreEqual(3, afterLast.Data!.PageNumber);
        Assert.IsTrue(afterLast.Data.WasClamped);
        StringAssert.Contains(afterLast.Data.Content, "Last (third) page content");
    }

    /// <summary>
    /// AC8 (Boundary): Rejects duplicate book uploads (409) and duplicate bookmark names (409) while allowing subsequent valid actions.
    /// </summary>
    [TestMethod]
    public async Task AC8_Boundary_DuplicateBookAndDuplicateBookmarkRejectedAndSystemContinuesWorking()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var user = (await auth.RegisterAsync("duplicate_user", "Password123")).Data!.User;

        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3");
        var firstUpload = await books.UploadBookAsync(user.Id, "book.pdf", pdfBytes);
        Assert.IsTrue(firstUpload.IsSuccess);
        var bookId = firstUpload.Data!.Id;

        // Re-uploading the same file content for the same user must return 409 DuplicateBook.
        var duplicateUpload = await books.UploadBookAsync(user.Id, "book.pdf", pdfBytes);
        Assert.IsFalse(duplicateUpload.IsSuccess);
        Assert.AreEqual(409, duplicateUpload.StatusCode);
        Assert.AreEqual(EbookErrorCodes.DuplicateBook, duplicateUpload.ErrorCode);

        // Creating a bookmark with an already-used name must return 409 DuplicateBookmark.
        var firstBm = await books.AddBookmarkAsync(user.Id, bookId, "Chapter 1", 1);
        Assert.IsTrue(firstBm.IsSuccess);

        var duplicateBm = await books.AddBookmarkAsync(user.Id, bookId, "Chapter 1", 2);
        Assert.IsFalse(duplicateBm.IsSuccess);
        Assert.AreEqual(409, duplicateBm.StatusCode);
        Assert.AreEqual(EbookErrorCodes.DuplicateBookmark, duplicateBm.ErrorCode);

        // Subsequent non-duplicate bookmark creation continues to succeed normally.
        var secondBm = await books.AddBookmarkAsync(user.Id, bookId, "Page 3 note", 3);
        Assert.IsTrue(secondBm.IsSuccess);
        Assert.AreEqual(3, secondBm.Data!.PageNumber);
    }

    /// <summary>
    /// Verifies EPUB Table of Contents extraction (titles and 1-based page numbers) and jumping to a TOC section page.
    /// </summary>
    [TestMethod]
    public async Task Reader_TableOfContentsExtractedAndNavigable()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var user = (await auth.RegisterAsync("toc_user", "Password123")).Data!.User;

        // Build an EPUB with 3 chapters and matching navigation TOC labels.
        var epubBytes = TestEbookFactory.CreateValidEpubBytesWithMetadata(
            "TOC Book",
            "Author A",
            ["Prologue", "Main Story", "Epilogue"],
            "Opening scene content",
            "Middle chapter content",
            "Closing scene content");

        // Verify extracted TOC entries map sequentially to the corresponding chapter pages.
        var uploaded = (await books.UploadBookAsync(user.Id, "toc.epub", epubBytes)).Data!;
        Assert.IsNotNull(uploaded.TableOfContents);
        Assert.AreEqual(3, uploaded.TableOfContents.Count);
        Assert.AreEqual("Prologue", uploaded.TableOfContents[0].Title);
        Assert.AreEqual(1, uploaded.TableOfContents[0].PageNumber);
        Assert.AreEqual("Main Story", uploaded.TableOfContents[1].Title);
        Assert.AreEqual(2, uploaded.TableOfContents[1].PageNumber);
        Assert.AreEqual("Epilogue", uploaded.TableOfContents[2].Title);
        Assert.AreEqual(3, uploaded.TableOfContents[2].PageNumber);

        // Navigate using the second TOC entry and verify its chapter content.
        var tocJump = await books.GetBookPageAsync(user.Id, uploaded.Id, uploaded.TableOfContents[1].PageNumber);
        Assert.IsTrue(tocJump.IsSuccess);
        StringAssert.Contains(tocJump.Data!.Content, "Middle chapter content");
    }

    /// <summary>
    /// Verifies persistent <c>ZoomLevel</c>, zoom clamping to [50, 300], and page clamping inside <c>UpdateReadingProgressAsync</c>.
    /// </summary>
    [TestMethod]
    public async Task Reader_ZoomLevelPersistsAndClampsAndProgressPageClamps()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var user = (await auth.RegisterAsync("zoom_user", "Password123")).Data!.User;

        // Default zoom level on upload must be 100%.
        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3");
        var book = (await books.UploadBookAsync(user.Id, "zoom.pdf", pdfBytes)).Data!;
        Assert.AreEqual(100, book.ZoomLevel);

        // Update zoom to 150% and confirm persistence in both detail and library summary DTOs.
        var updated = await books.UpdateReadingProgressAsync(user.Id, book.Id, 2, zoomLevel: 150);
        Assert.IsTrue(updated.IsSuccess);
        Assert.AreEqual(150, updated.Data!.ZoomLevel);
        Assert.IsFalse(updated.Data.WasClamped);

        var detail = (await books.GetBookDetailAsync(user.Id, book.Id)).Data!;
        Assert.AreEqual(150, detail.ZoomLevel);

        var summary = (await books.GetUserBooksAsync(user.Id))[0];
        Assert.AreEqual(150, summary.ZoomLevel);

        // Lower-bound clamping: page 0 -> 1 (WasClamped = true), zoom 10 -> 50.
        var lowZoomAndPage = await books.UpdateReadingProgressAsync(user.Id, book.Id, 0, zoomLevel: 10);
        Assert.IsTrue(lowZoomAndPage.IsSuccess);
        Assert.AreEqual(1, lowZoomAndPage.Data!.CurrentPage);
        Assert.IsTrue(lowZoomAndPage.Data.WasClamped);
        Assert.AreEqual(50, lowZoomAndPage.Data.ZoomLevel);

        // Upper-bound clamping: page 999 -> TotalPages (WasClamped = true), zoom 500 -> 300.
        var highZoomAndPage = await books.UpdateReadingProgressAsync(user.Id, book.Id, 999, zoomLevel: 500);
        Assert.IsTrue(highZoomAndPage.IsSuccess);
        Assert.AreEqual(3, highZoomAndPage.Data!.CurrentPage);
        Assert.IsTrue(highZoomAndPage.Data.WasClamped);
        Assert.AreEqual(300, highZoomAndPage.Data.ZoomLevel);
    }

    /// <summary>
    /// Verifies bookmark name validation, case-insensitive and same-page duplicate rejection (409),
    /// cross-user delete protection (403), missing bookmark (404), and owner deletion.
    /// </summary>
    [TestMethod]
    public async Task Bookmarks_ValidationCaseInsensitiveDuplicatesAndDeletion()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var owner = (await auth.RegisterAsync("bm_owner", "Password123")).Data!.User;
        var intruder = (await auth.RegisterAsync("bm_intruder", "Password123")).Data!.User;

        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3");
        var book = (await books.UploadBookAsync(owner.Id, "bm.pdf", pdfBytes)).Data!;

        // Whitespace-only bookmark name must return 400 InvalidBookmarkName.
        var emptyName = await books.AddBookmarkAsync(owner.Id, book.Id, "   ", 1);
        Assert.IsFalse(emptyName.IsSuccess);
        Assert.AreEqual(400, emptyName.StatusCode);
        Assert.AreEqual(EbookErrorCodes.InvalidBookmarkName, emptyName.ErrorCode);

        // Add initial bookmark, then verify both case-insensitive name and same-page duplicates are rejected (409).
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

        // Another user cannot delete the bookmark (403), and unknown bookmark IDs return 404.
        var forbiddenDelete = await books.DeleteBookmarkAsync(intruder.Id, book.Id, first.Data!.Id);
        Assert.IsFalse(forbiddenDelete.IsSuccess);
        Assert.AreEqual(403, forbiddenDelete.StatusCode);
        Assert.AreEqual(EbookErrorCodes.ForbiddenBookAccess, forbiddenDelete.ErrorCode);

        var missingDelete = await books.DeleteBookmarkAsync(owner.Id, book.Id, Guid.NewGuid());
        Assert.IsFalse(missingDelete.IsSuccess);
        Assert.AreEqual(404, missingDelete.StatusCode);
        Assert.AreEqual(EbookErrorCodes.BookmarkNotFound, missingDelete.ErrorCode);

        // Owner deletion removes the bookmark cleanly from book details.
        var deleted = await books.DeleteBookmarkAsync(owner.Id, book.Id, first.Data.Id);
        Assert.IsTrue(deleted.IsSuccess);
        Assert.AreEqual(0, (await books.GetBookDetailAsync(owner.Id, book.Id)).Data!.Bookmarks.Count);
    }
}
