namespace EbookReader.Backend.Services;

using System.IO.Compression;
using System.Text;
using EbookReader.Backend.Models;
using VersOne.Epub;

/// <summary>
/// Parser and validator for EPUB e-book archives using <see cref="VersOne.Epub"/>.
/// </summary>
public static class EpubEbookParser
{
    /// <summary>
    /// Validates ZIP header and CRC integrity, then parses chapters, styles, images, and navigation TOC.
    /// </summary>
    /// <param name="fileBytes">Raw byte array of the EPUB file.</param>
    /// <param name="contentHash">Precomputed SHA-256 hash of the file bytes.</param>
    /// <returns>A <see cref="ServiceResult{T}"/> containing <see cref="ParsedEbook"/> or EPUB corruption error.</returns>
    public static ServiceResult<ParsedEbook> ParseAndValidate(byte[] fileBytes, string contentHash)
    {
        // Validation guard: ZIP local file header signature 'PK\x03\x04'
        if (fileBytes.Length < 4 ||
            fileBytes[0] != (byte)'P' ||
            fileBytes[1] != (byte)'K' ||
            fileBytes[2] != 0x03 ||
            fileBytes[3] != 0x04)
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.CorruptedEpubHeader,
                "Corrupted EPUB file: invalid ZIP/EPUB archive.");
        }

        try
        {
            // Verify structural archive integrity and CRC checks
            var zipCheck = ValidateZipArchive(fileBytes);
            if (!zipCheck.IsSuccess)
                return ServiceResult<ParsedEbook>.Failure(zipCheck.StatusCode, zipCheck.ErrorCode!, zipCheck.ErrorMessage!);

            using var ms = new MemoryStream(fileBytes, writable: false);
            EpubBook? epubBook = null;
            try { epubBook = EpubReader.ReadBook(ms); } catch { epubBook = null; }

            if (epubBook == null)
            {
                return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.CorruptedEpubStructure, "Corrupted EPUB structure: failed to parse EPUB document.");
            }

            if (epubBook.ReadingOrder == null || epubBook.ReadingOrder.Count == 0)
            {
                return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.EmptyEpubContent, "The EPUB file does not contain readable text chapters.");
            }

            // Extract metadata title and author
            string? title = !string.IsNullOrWhiteSpace(epubBook.Title) ? epubBook.Title.Trim() : null;
            string? author = !string.IsNullOrWhiteSpace(epubBook.Author)
                ? epubBook.Author.Trim()
                : (epubBook.AuthorList is { Count: > 0 } ? string.Join(", ", epubBook.AuthorList.Where(a => !string.IsNullOrWhiteSpace(a))) : null);

            var combinedCss = ExtractCss(epubBook);
            var imageMap = ExtractImageMap(epubBook);
            var chapterPageMap = new Dictionary<string, (int StartPage, int EndPage)>(StringComparer.OrdinalIgnoreCase);
            var pages = new List<string>();

            // Paginate each chapter in the reading order
            foreach (var chapter in epubBook.ReadingOrder)
            {
                if (chapter == null || string.IsNullOrWhiteSpace(chapter.Content)) continue;
                int startPage = pages.Count + 1;
                foreach (var cp in HtmlPaginator.FormatChapterHtml(chapter.Content, imageMap, combinedCss))
                {
                    if (!string.IsNullOrWhiteSpace(cp)) pages.Add(cp);
                }
                int endPage = Math.Max(startPage, pages.Count);

                // Map chapter file paths to 1-based page ranges for navigation lookup
                if (!string.IsNullOrWhiteSpace(chapter.FilePath))
                {
                    var clean = chapter.FilePath.Replace('\\', '/').TrimStart('/');
                    chapterPageMap[chapter.FilePath] = (startPage, endPage);
                    chapterPageMap[clean] = (startPage, endPage);
                    chapterPageMap[Path.GetFileName(clean)] = (startPage, endPage);
                }
            }

            // Fail if no readable chapter pages were generated
            if (pages.Count == 0)
            {
                return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.EmptyEpubContent, "The EPUB file does not contain readable text chapters.");
            }

            var toc = new List<TocItem>();

            // Build TOC from formal EPUB navigation items
            if (epubBook.Navigation is { Count: > 0 })
                EpubTocExtractor.BuildNavigationToc(epubBook.Navigation, chapterPageMap, pages, toc, 0);

            // Fall back to scanning HTML headings if navigation is missing
            if (toc.Count == 0)
                EpubTocExtractor.BuildHeadingToc(epubBook.ReadingOrder, chapterPageMap, pages, toc);

            return ServiceResult<ParsedEbook>.Success(new ParsedEbook("EPUB", contentHash, pages, title, author, toc));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.CorruptedEpubArchive, $"Corrupted EPUB archive: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies that all entries in the ZIP archive can be opened and read without CRC/decompression corruption.
    /// </summary>
    private static ServiceResult<bool> ValidateZipArchive(byte[] fileBytes)
    {
        using var zipStream = new MemoryStream(fileBytes, writable: false);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count == 0)
            return ServiceResult<bool>.Failure(400, EbookErrorCodes.EmptyEpubArchive, "Corrupted EPUB file: the archive is empty.");

        foreach (var entry in archive.Entries)
        {
            using var entryStream = entry.Open();
            var buffer = new byte[1024];
            while (entryStream.Read(buffer, 0, buffer.Length) > 0) { }
        }
        return ServiceResult<bool>.Success(true);
    }

    /// <summary>
    /// Combines all CSS stylesheets declared within the EPUB.
    /// </summary>
    private static string ExtractCss(EpubBook epubBook)
    {
        var cssSb = new StringBuilder();
        if (epubBook.Content?.Css?.Local != null)
        {
            foreach (var css in epubBook.Content.Css.Local)
            {
                if (!string.IsNullOrWhiteSpace(css?.Content)) cssSb.AppendLine(css.Content);
            }
        }
        return cssSb.ToString().Trim();
    }

    /// <summary>
    /// Extracts local image files from the EPUB and converts them into Base64 data URIs.
    /// </summary>
    private static Dictionary<string, string> ExtractImageMap(EpubBook epubBook)
    {
        var imageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (epubBook.Content?.Images?.Local == null) return imageMap;

        foreach (var img in epubBook.Content.Images.Local)
        {
            if (img?.Content is { Length: > 0 })
            {
                var mime = !string.IsNullOrWhiteSpace(img.ContentMimeType) ? img.ContentMimeType : "image/jpeg";
                var uri = $"data:{mime};base64,{Convert.ToBase64String(img.Content)}";
                if (!string.IsNullOrWhiteSpace(img.FilePath))
                {
                    imageMap[img.FilePath] = uri;
                    imageMap[img.FilePath.Replace('\\', '/').TrimStart('/')] = uri;
                    imageMap[Path.GetFileName(img.FilePath)] = uri;
                }
                if (!string.IsNullOrWhiteSpace(img.Key)) imageMap[img.Key] = uri;
            }
        }
        return imageMap;
    }
}
