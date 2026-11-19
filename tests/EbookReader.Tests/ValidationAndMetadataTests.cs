using System.Text;
using EbookReader.Backend.Models;
using EbookReader.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EbookReader.Tests;

/// <summary>
/// Verifies file structural integrity checks (<see cref="IEbookValidatorAndParser"/>) and metadata title/author extraction.
/// </summary>
[TestClass]
public class ValidationAndMetadataTests
{
    /// <summary>
    /// Verifies rejection of empty files, blank filenames, missing PDF headers/pages, non-ZIP EPUB headers, and ZIPs missing OPF structure.
    /// </summary>
    [TestMethod]
    public void Validator_RejectsEmptyFileInvalidFilenameAndCorruptedHeadersOrStructure()
    {
        var (_, _, validator) = TestServiceFactory.CreateServices();

        // Empty payload must return 400 EmptyFile.
        var emptyResult = validator.ValidateAndParse("book.pdf", []);
        Assert.IsFalse(emptyResult.IsSuccess);
        Assert.AreEqual(400, emptyResult.StatusCode);
        Assert.AreEqual(EbookErrorCodes.EmptyFile, emptyResult.ErrorCode);

        // Blank filename must return 400 InvalidFilename.
        var badNameResult = validator.ValidateAndParse("   ", [1, 2, 3]);
        Assert.IsFalse(badNameResult.IsSuccess);
        Assert.AreEqual(400, badNameResult.StatusCode);
        Assert.AreEqual(EbookErrorCodes.InvalidFilename, badNameResult.ErrorCode);

        // Non-PDF header bytes must be rejected with CorruptedPdfHeader or CorruptedPdf.
        var badPdfHeader = validator.ValidateAndParse("bad.pdf", Encoding.ASCII.GetBytes("NOT_A_PDF%%EOF"));
        Assert.IsFalse(badPdfHeader.IsSuccess);
        Assert.AreEqual(400, badPdfHeader.StatusCode);
        Assert.IsTrue(badPdfHeader.ErrorCode is EbookErrorCodes.CorruptedPdfHeader or EbookErrorCodes.CorruptedPdf);

        // PDF lacking any /Page objects must return 400 CorruptedPdfStructure.
        var noPagePdf = validator.ValidateAndParse("nopage.pdf", Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n%%EOF"));
        Assert.IsFalse(noPagePdf.IsSuccess);
        Assert.AreEqual(400, noPagePdf.StatusCode);
        Assert.AreEqual(EbookErrorCodes.CorruptedPdfStructure, noPagePdf.ErrorCode);

        // Non-ZIP bytes for an .epub file must return 400 CorruptedEpubHeader.
        var badEpubHeader = validator.ValidateAndParse("bad.epub", Encoding.ASCII.GetBytes("NOT_A_ZIP_FILE"));
        Assert.IsFalse(badEpubHeader.IsSuccess);
        Assert.AreEqual(400, badEpubHeader.StatusCode);
        Assert.AreEqual(EbookErrorCodes.CorruptedEpubHeader, badEpubHeader.ErrorCode);

        // Valid ZIP archive lacking container.xml / OPF must return 400 CorruptedEpubStructure.
        var zipMissingOpf = validator.ValidateAndParse("incomplete.epub", TestEbookFactory.CreateZipBytes("readme.txt", "hello"));
        Assert.IsFalse(zipMissingOpf.IsSuccess);
        Assert.AreEqual(400, zipMissingOpf.StatusCode);
        Assert.AreEqual(EbookErrorCodes.CorruptedEpubStructure, zipMissingOpf.ErrorCode);
    }

    /// <summary>
    /// Verifies PDF/EPUB embedded Title/Author metadata extraction, filename title fallback,
    /// and the explicit <c>customTitle</c> rule where <c>Author</c> falls back to <c>fileName</c>.
    /// </summary>
    [TestMethod]
    public async Task Library_MetadataExtractionAndFilenameFallback()
    {
        var (auth, books, _) = TestServiceFactory.CreateServices();
        var user = (await auth.RegisterAsync("meta_user", "Password123")).Data!.User;

        // PDF without customTitle extracts embedded /Info Title and Author.
        var pdfWithMeta = TestEbookFactory.CreateValidPdfBytesWithMetadata("Clean Architecture", "Robert Martin", "Chapter 1");
        var pdfUpload = await books.UploadBookAsync(user.Id, "raw_filename.pdf", pdfWithMeta);
        Assert.IsTrue(pdfUpload.IsSuccess);
        Assert.AreEqual("Clean Architecture", pdfUpload.Data!.Title);
        Assert.AreEqual("Robert Martin", pdfUpload.Data.Author);

        // EPUB without customTitle extracts embedded <dc:title> and <dc:creator>.
        var epubWithMeta = TestEbookFactory.CreateValidEpubBytesWithMetadata("Domain-Driven Design", "Eric Evans", null, "Intro");
        var epubUpload = await books.UploadBookAsync(user.Id, "ddd_file.epub", epubWithMeta);
        Assert.IsTrue(epubUpload.IsSuccess);
        Assert.AreEqual("Domain-Driven Design", epubUpload.Data!.Title);
        Assert.AreEqual("Eric Evans", epubUpload.Data.Author);

        // PDF without embedded Title falls back to filename without extension.
        var pdfNoTitle = TestEbookFactory.CreateValidPdfBytes("Just a page");
        var fallbackUpload = await books.UploadBookAsync(user.Id, "fallback_title.pdf", pdfNoTitle);
        Assert.IsTrue(fallbackUpload.IsSuccess);
        Assert.AreEqual("fallback_title", fallbackUpload.Data!.Title);

        // When customTitle is explicitly supplied, Title uses customTitle and Author uses fileName (even if embedded MetadataAuthor exists).
        var customPdf = TestEbookFactory.CreateValidPdfBytesWithMetadata("Embedded PDF Title", "Embedded PDF Author", "Custom PDF page");
        var customPdfUpload = await books.UploadBookAsync(user.Id, "custom_pdf_file.pdf", customPdf, customTitle: "Custom Book Title");
        Assert.IsTrue(customPdfUpload.IsSuccess);
        Assert.AreEqual("Custom Book Title", customPdfUpload.Data!.Title);
        Assert.AreEqual("custom_pdf_file.pdf", customPdfUpload.Data.Author);

        var customEpub = TestEbookFactory.CreateValidEpubBytesWithMetadata("Embedded EPUB Title", "Embedded EPUB Author", null, "Custom EPUB chapter");
        var customEpubUpload = await books.UploadBookAsync(user.Id, "custom_epub_file.epub", customEpub, customTitle: "Custom Book Title");
        Assert.IsTrue(customEpubUpload.IsSuccess);
        Assert.AreEqual("Custom Book Title", customEpubUpload.Data!.Title);
        Assert.AreEqual("custom_epub_file.epub", customEpubUpload.Data.Author);
    }
}
