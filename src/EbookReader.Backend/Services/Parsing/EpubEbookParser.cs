namespace EbookReader.Backend.Services;

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using EbookReader.Backend.Models;
using VersOne.Epub;

public static class EpubEbookParser
{
    public static ServiceResult<ParsedEbook> ParseAndValidate(byte[] fileBytes, string contentHash)
    {
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

            string? title = !string.IsNullOrWhiteSpace(epubBook.Title) ? epubBook.Title.Trim() : null;
            string? author = !string.IsNullOrWhiteSpace(epubBook.Author)
                ? epubBook.Author.Trim()
                : (epubBook.AuthorList is { Count: > 0 } ? string.Join(", ", epubBook.AuthorList.Where(a => !string.IsNullOrWhiteSpace(a))) : null);

            var combinedCss = ExtractCss(epubBook);
            var imageMap = ExtractImageMap(epubBook);
            var chapterPageMap = new Dictionary<string, (int StartPage, int EndPage)>(StringComparer.OrdinalIgnoreCase);
            var pages = new List<string>();

            foreach (var chapter in epubBook.ReadingOrder)
            {
                if (chapter == null || string.IsNullOrWhiteSpace(chapter.Content)) continue;
                int startPage = pages.Count + 1;
                foreach (var cp in HtmlPaginator.FormatChapterHtml(chapter.Content, imageMap, combinedCss))
                {
                    if (!string.IsNullOrWhiteSpace(cp)) pages.Add(cp);
                }
                int endPage = Math.Max(startPage, pages.Count);

                if (!string.IsNullOrWhiteSpace(chapter.FilePath))
                {
                    var clean = chapter.FilePath.Replace('\\', '/').TrimStart('/');
                    chapterPageMap[chapter.FilePath] = (startPage, endPage);
                    chapterPageMap[clean] = (startPage, endPage);
                    chapterPageMap[Path.GetFileName(clean)] = (startPage, endPage);
                }
            }

            if (pages.Count == 0)
            {
                return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.EmptyEpubContent, "The EPUB file does not contain readable text chapters.");
            }

            var toc = new List<TocItem>();
            if (epubBook.Navigation is { Count: > 0 })
                BuildNavigationToc(epubBook.Navigation, chapterPageMap, pages, toc, 0);

            if (toc.Count == 0)
                BuildHeadingToc(epubBook.ReadingOrder, chapterPageMap, pages, toc);

            return ServiceResult<ParsedEbook>.Success(new ParsedEbook("EPUB", contentHash, pages, title, author, toc));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.CorruptedEpubArchive, $"Corrupted EPUB archive: {ex.Message}");
        }
    }

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

    private static void BuildNavigationToc(
        IEnumerable<EpubNavigationItem> navItems,
        Dictionary<string, (int StartPage, int EndPage)> chapterPageMap,
        List<string> pages,
        List<TocItem> toc,
        int level)
    {
        foreach (var item in navItems)
        {
            if (item == null) continue;
            string? filePath = item.Link?.ContentFilePath ?? item.HtmlContentFile?.FilePath;
            int page = ResolvePageForPath(filePath, item.Link?.Anchor, chapterPageMap, pages);

            if (!string.IsNullOrWhiteSpace(item.Title))
            {
                toc.Add(new TocItem
                {
                    Title = item.Title.Trim(),
                    PageNumber = Math.Clamp(page, 1, Math.Max(1, pages.Count)),
                    Level = level
                });
            }

            if (item.NestedItems is { Count: > 0 })
                BuildNavigationToc(item.NestedItems, chapterPageMap, pages, toc, level + 1);
        }
    }

    private static int ResolvePageForPath(
        string? filePath,
        string? anchor,
        Dictionary<string, (int StartPage, int EndPage)> chapterPageMap,
        List<string> pages)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return 1;
        if (filePath.Contains('#') && string.IsNullOrWhiteSpace(anchor))
        {
            var parts = filePath.Split('#', 2);
            filePath = parts[0];
            anchor = parts[1];
        }

        var clean = filePath.Replace('\\', '/').TrimStart('/');
        var fileName = Path.GetFileName(clean);

        if (!chapterPageMap.TryGetValue(filePath, out var range) &&
            !chapterPageMap.TryGetValue(clean, out range) &&
            !chapterPageMap.TryGetValue(fileName, out range))
        {
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(anchor))
        {
            for (int p = range.StartPage; p <= range.EndPage && p <= pages.Count; p++)
            {
                var content = pages[p - 1];
                if (content.Contains($"id=\"{anchor}\"", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains($"name=\"{anchor}\"", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains($"id='{anchor}'", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains($"name='{anchor}'", StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
        }

        return range.StartPage;
    }

    private static void BuildHeadingToc(
        IEnumerable<EpubLocalTextContentFile> readingOrder,
        Dictionary<string, (int StartPage, int EndPage)> chapterPageMap,
        List<string> pages,
        List<TocItem> toc)
    {
        foreach (var chapter in readingOrder)
        {
            if (chapter != null && !string.IsNullOrWhiteSpace(chapter.FilePath) &&
                chapterPageMap.TryGetValue(Path.GetFileName(chapter.FilePath), out var range))
            {
                for (int p = range.StartPage; p <= range.EndPage && p <= pages.Count; p++)
                {
                    var hMatch = Regex.Match(pages[p - 1], @"<h([1-3])[^>]*>(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (hMatch.Success)
                    {
                        var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(hMatch.Groups[2].Value, @"<[^>]+>", string.Empty)).Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            toc.Add(new TocItem { Title = text, PageNumber = p, Level = Math.Max(0, int.Parse(hMatch.Groups[1].Value) - 1) });
                            break;
                        }
                    }
                }
            }
        }
    }
}
