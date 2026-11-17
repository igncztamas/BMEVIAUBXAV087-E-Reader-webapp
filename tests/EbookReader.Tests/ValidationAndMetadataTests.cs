using System.Text;
using EbookReader.Backend.Models;
using EbookReader.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EbookReader.Tests;

[TestClass]
public class ValidationAndMetadataTests
{
    [TestMethod]
    public void Validator_RejectsEmptyFileInvalidFilenameAndCorruptedHeadersOrStructure()
    {
        var (_, _, validator) = TestServiceFactory.CreateServices();

        var emptyResult = validator.ValidateAndParse("book.pdf", []);
        Assert.IsFalse(emptyResult.IsSuccess);
        Assert.AreEqual(400, emptyResult.StatusCode);
        Assert.AreEqual(EbookErrorCodes.EmptyFile, emptyResult.ErrorCode);

        var badNameResult = validator.ValidateAndParse("   ", [1, 2, 3]);
        Assert.IsFalse(badNameResult.IsSuccess);
        Assert.AreEqual(400, badNameResult.StatusCode);
        Assert.AreEqual(EbookErrorCodes.InvalidFilename, badNameResult.ErrorCode);

        var badPdfHeader = validator.ValidateAndParse("bad.pdf", Encoding.ASCII.GetBytes("NOT_A_PDF%%EOF"));
        Assert.IsFalse(badPdfHeader.IsSuccess);
        Assert.AreEqual(400, badPdfHeader.StatusCode);
        Assert.IsTrue(badPdfHeader.ErrorCode is EbookErrorCodes.CorruptedPdfHeader or EbookErrorCodes.CorruptedPdf);

        var noPagePdf = validator.ValidateAndParse("nopage.pdf", Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF"));
        Assert.IsFalse(noPagePdf.IsSuccess);
        Assert.AreEqual(400, noPagePdf.StatusCode);
        Assert.AreEqual(EbookErrorCodes.CorruptedPdfStructure, noPagePdf.ErrorCode);

        var badEpubHeader = validator.ValidateAndParse("bad.epub", Encoding.ASCII.GetBytes("NOT_A_ZIP_FILE"));
        Assert.IsFalse(badEpubHeader.IsSuccess);
        Assert.AreEqual(400, badEpubHeader.StatusCode);
        Assert.AreEqual(EbookErrorCodes.CorruptedEpubHeader, badEpubHeader.ErrorCode);

        var zipMissingOpf = validator.ValidateAndParse("incomplete.epub", TestEbookFactory.CreateZipBytes("readme.txt", "hello"));
        Assert.IsFalse(zipMissingOpf.IsSuccess);
        Assert.AreEqual(400, zipMissingOpf.StatusCode);
        Assert.AreEqual(EbookErrorCodes.CorruptedEpubStructure, zipMissingOpf.ErrorCode);
    }

    [TestMethod]
    public async Task Library_MetadataExtractionAndFilenameFallback()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var user = (await auth.RegisterAsync("meta_user", "Password123")).Data!.User;

        var pdfWithMeta = TestEbookFactory.CreateValidPdfBytesWithMetadata("Clean Architecture", "Robert Martin", "Chapter 1");
        var pdfUpload = await books.UploadBookAsync(user.Id, "raw_filename.pdf", pdfWithMeta);
        Assert.IsTrue(pdfUpload.IsSuccess);
        Assert.AreEqual("Clean Architecture", pdfUpload.Data!.Title);
        Assert.AreEqual("Robert Martin", pdfUpload.Data.Author);

        var epubWithMeta = TestEbookFactory.CreateValidEpubBytesWithMetadata("Domain-Driven Design", "Eric Evans", null, "Intro");
        var epubUpload = await books.UploadBookAsync(user.Id, "ddd_file.epub", epubWithMeta);
        Assert.IsTrue(epubUpload.IsSuccess);
        Assert.AreEqual("Domain-Driven Design", epubUpload.Data!.Title);
        Assert.AreEqual("Eric Evans", epubUpload.Data.Author);

        var pdfNoTitle = TestEbookFactory.CreateValidPdfBytes("Just a page");
        var fallbackUpload = await books.UploadBookAsync(user.Id, "fallback_title.pdf", pdfNoTitle);
        Assert.IsTrue(fallbackUpload.IsSuccess);
        Assert.AreEqual("fallback_title", fallbackUpload.Data!.Title);
    }
}
