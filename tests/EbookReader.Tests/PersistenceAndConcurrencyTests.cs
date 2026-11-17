using EbookReader.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EbookReader.Tests;

[TestClass]
public class PersistenceAndConcurrencyTests
{
    [TestMethod]
    public async Task Concurrency_LastWriteWins_IgnoresOutdatedPositionUpdates()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();

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
    public async Task Concurrency_LastWriteWins_PreservesFlagsTimestampAndZoomLevel()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var user = (await auth.RegisterAsync("lww_user", "Password123")).Data!.User;
        var pdfBytes = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3", "Page 4", "Page 5");
        var book = (await books.UploadBookAsync(user.Id, "lww.pdf", pdfBytes)).Data!;

        var t1 = DateTimeOffset.UtcNow.AddMinutes(1);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(5);

        var newer = await books.UpdateReadingProgressAsync(user.Id, book.Id, 4, t2, zoomLevel: 160);
        Assert.IsTrue(newer.IsSuccess);
        Assert.IsTrue(newer.Data!.WasUpdated);
        Assert.AreEqual(t2, newer.Data.LastPositionUpdatedAtUtc);
        Assert.AreEqual(160, newer.Data.ZoomLevel);

        var stale = await books.UpdateReadingProgressAsync(user.Id, book.Id, 2, t1, zoomLevel: 80);
        Assert.IsTrue(stale.IsSuccess);
        Assert.IsFalse(stale.Data!.WasUpdated);
        Assert.AreEqual(4, stale.Data.CurrentPage);
        Assert.AreEqual(160, stale.Data.ZoomLevel);
        Assert.AreEqual(t2, stale.Data.LastPositionUpdatedAtUtc);

        var equalTimestamp = await books.UpdateReadingProgressAsync(user.Id, book.Id, 5, t2, zoomLevel: 175);
        Assert.IsTrue(equalTimestamp.IsSuccess);
        Assert.IsTrue(equalTimestamp.Data!.WasUpdated);
        Assert.AreEqual(5, equalTimestamp.Data.CurrentPage);
        Assert.AreEqual(175, equalTimestamp.Data.ZoomLevel);
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
                var (auth1, books1, _) = TestServiceFactory.CreateServices(tempFile);
                var reg = await auth1.RegisterAsync("persistent_user", "Password123");
                userId = reg.Data!.User.Id;

                var pdf = TestEbookFactory.CreateValidPdfBytes("Page 1", "Page 2", "Page 3");
                var uploaded = await books1.UploadBookAsync(userId, "durable.pdf", pdf, "Durable Book");
                bookId = uploaded.Data!.Id;

                await books1.UpdateReadingProgressAsync(userId, bookId, 2, zoomLevel: 140);
                await books1.AddBookmarkAsync(userId, bookId, "Important page", 2);
            }

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
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
