using EbookReader.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EbookReader.Tests;

/// <summary>
/// Verifies Last-Write-Wins concurrency resolution by timestamp and JSON data persistence across store reloads.
/// </summary>
[TestClass]
public class PersistenceAndConcurrencyTests
{
    /// <summary>
    /// Ensures an outdated timestamped position update does not overwrite a newer saved reading position.
    /// </summary>
    [TestMethod]
    public async Task Concurrency_LastWriteWins_IgnoresOutdatedPositionUpdates()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();

        var user = (await auth.RegisterAsync("multi_device_user", "Password123")).Data!.User;
        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3", "Page 4", "Page 5");
        var book = (await books.UploadBookAsync(user.Id, "multi.pdf", pdfBytes)).Data!;

        // Save newer timestamp t2 first, then send older timestamp t1.
        var t1 = DateTimeOffset.UtcNow.AddMinutes(1);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(5);

        var newerUpdate = await books.UpdateReadingProgressAsync(user.Id, book.Id, 4, t2);
        Assert.IsTrue(newerUpdate.IsSuccess);
        Assert.AreEqual(4, newerUpdate.Data!.CurrentPage);

        var staleUpdate = await books.UpdateReadingProgressAsync(user.Id, book.Id, 2, t1);
        Assert.IsTrue(staleUpdate.IsSuccess);

        // Verify the newer page (4) remained intact.
        var detail = (await books.GetBookDetailAsync(user.Id, book.Id)).Data!;
        Assert.AreEqual(4, detail.CurrentPage, "An older timestamped (stale) position save must not overwrite a newer position!");
    }

    /// <summary>
    /// Verifies <c>WasUpdated</c>, <c>LastPositionUpdatedAtUtc</c>, <c>ZoomLevel</c>, and equal-timestamp (<c>&gt;=</c>) LWW behavior.
    /// </summary>
    [TestMethod]
    public async Task Concurrency_LastWriteWins_PreservesFlagsTimestampAndZoomLevel()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var user = (await auth.RegisterAsync("lww_user", "Password123")).Data!.User;
        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3", "Page 4", "Page 5");
        var book = (await books.UploadBookAsync(user.Id, "lww.pdf", pdfBytes)).Data!;

        var t1 = DateTimeOffset.UtcNow.AddMinutes(1);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(5);

        // Apply newer update at t2 -> WasUpdated must be true.
        var newer = await books.UpdateReadingProgressAsync(user.Id, book.Id, 4, t2, zoomLevel: 160);
        Assert.IsTrue(newer.IsSuccess);
        Assert.IsTrue(newer.Data!.WasUpdated);
        Assert.AreEqual(t2, newer.Data.LastPositionUpdatedAtUtc);
        Assert.AreEqual(160, newer.Data.ZoomLevel);

        // Apply stale update at t1 -> WasUpdated must be false and stored page, zoom, and timestamp must not regress.
        var stale = await books.UpdateReadingProgressAsync(user.Id, book.Id, 2, t1, zoomLevel: 80);
        Assert.IsTrue(stale.IsSuccess);
        Assert.IsFalse(stale.Data!.WasUpdated);
        Assert.AreEqual(4, stale.Data.CurrentPage);
        Assert.AreEqual(160, stale.Data.ZoomLevel);
        Assert.AreEqual(t2, stale.Data.LastPositionUpdatedAtUtc);

        // Apply update with equal timestamp t2 -> >= semantics must accept the update.
        var equalTimestamp = await books.UpdateReadingProgressAsync(user.Id, book.Id, 5, t2, zoomLevel: 175);
        Assert.IsTrue(equalTimestamp.IsSuccess);
        Assert.IsTrue(equalTimestamp.Data!.WasUpdated);
        Assert.AreEqual(5, equalTimestamp.Data.CurrentPage);
        Assert.AreEqual(175, equalTimestamp.Data.ZoomLevel);
    }

    /// <summary>
    /// Verifies user credentials, uploaded books, reading progress, zoom level, and bookmarks survive a full JSON store reload.
    /// </summary>
    [TestMethod]
    public async Task Persistence_DataSurvivesStoreReload()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"ebook_test_{Guid.NewGuid():N}.json");
        try
        {
            Guid userId;
            Guid bookId;

            // Session 1: register user, upload book, and save progress, zoom level, and bookmark to disk.
            {
                var (auth1, books1, _) = TestServiceFactory.CreateServices(tempFile);
                var reg = await auth1.RegisterAsync("persistent_user", "Password123");
                userId = reg.Data!.User.Id;

                var pdf = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3");
                var uploaded = await books1.UploadBookAsync(userId, "durable.pdf", pdf, "Durable Book");
                bookId = uploaded.Data!.Id;

                await books1.UpdateReadingProgressAsync(userId, bookId, 2, zoomLevel: 140);
                await books1.AddBookmarkAsync(userId, bookId, "Important page", 2);
            }

            // Session 2: spin up fresh services against the same JSON file and verify all persisted state.
            {
                var (auth2, books2, _) = TestServiceFactory.CreateServices(tempFile);
                var login = await auth2.LoginAsync("persistent_user", "Password123");
                Assert.IsTrue(login.IsSuccess);

                var detail = (await books2.GetBookDetailAsync(userId, bookId)).Data!;
                Assert.AreEqual("Durable Book", detail.Title);
                Assert.AreEqual(2, detail.CurrentPage);
                Assert.AreEqual(140, detail.ZoomLevel);
                Assert.AreEqual(1, detail.Bookmarks.Count);
                Assert.AreEqual("Important page", detail.Bookmarks[0].Name);
            }
        }
        finally
        {
            // Clean up the temporary JSON store file even if an assertion fails.
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
