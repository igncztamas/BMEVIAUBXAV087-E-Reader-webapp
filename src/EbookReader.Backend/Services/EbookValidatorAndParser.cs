using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EbookReader.Backend.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using VersOne.Epub;

namespace EbookReader.Backend.Services;

public class EbookValidatorAndParser : IEbookValidatorAndParser
{
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;

    public ServiceResult<ParsedEbook> ValidateAndParse(string fileName, byte[]? fileBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.InvalidFilename, "Filename is required.");
        }

        if (fileBytes is null || fileBytes.Length == 0)
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.EmptyFile, "The uploaded file is empty.");
        }

        if (fileBytes.Length > MaxFileSizeBytes)
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.FileTooLarge,
                $"File size ({fileBytes.Length} bytes) exceeds the allowed limit ({MaxFileSizeBytes} bytes).");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension != ".pdf" && extension != ".epub")
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.UnsupportedFormat,
                "Unsupported file format. Only PDF (.pdf) and EPUB (.epub) files are supported.");
        }

        var contentHash = Convert.ToHexString(SHA256.HashData(fileBytes));

        return extension == ".pdf"
            ? ParseAndValidatePdf(fileBytes, contentHash)
            : ParseAndValidateEpub(fileBytes, contentHash);
    }

    private static ServiceResult<ParsedEbook> ParseAndValidatePdf(byte[] fileBytes, string contentHash)
    {
        if (fileBytes.Length < 16)
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.CorruptedPdf,
                "Corrupted PDF file: the file is too short.");
        }

        var rawText = Encoding.Latin1.GetString(fileBytes);
        var utf8Text = Encoding.UTF8.GetString(fileBytes);

        if (!rawText.StartsWith("%PDF-", StringComparison.Ordinal))
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.CorruptedPdfHeader,
                "Corrupted PDF file: missing '%PDF-' header.");
        }

        var tailStart = Math.Max(0, rawText.Length - 1024);
        var tail = rawText[tailStart..];
        if (!tail.Contains("%%EOF", StringComparison.Ordinal))
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.CorruptedPdfEof,
                "Corrupted PDF structure: missing '%%EOF' marker.");
        }

        bool hasPageObject = rawText.Contains("/Type /Page", StringComparison.OrdinalIgnoreCase)
                             || rawText.Contains("/Type/Page", StringComparison.OrdinalIgnoreCase)
                             || rawText.Contains("/Pages", StringComparison.OrdinalIgnoreCase);

        if (!hasPageObject)
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.CorruptedPdfStructure,
                "Corrupted PDF structure: no valid page structure (/Page) found.");
        }

        PdfDocument? document = null;
        try
        {
            document = PdfDocument.Open(fileBytes, new ParsingOptions { UseLenientParsing = true });
        }
        catch
        {
            document = null;
        }

        if (document == null || document.NumberOfPages == 0)
        {
            document?.Dispose();

            if (hasPageObject)
            {
                var legacyPages = ExtractLegacyBtEtPages(utf8Text);
                if (legacyPages.Count > 0)
                {
                    return ServiceResult<ParsedEbook>.Success(new ParsedEbook("PDF", contentHash, legacyPages));
                }
            }

            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.CorruptedPdfStructure,
                "Corrupted PDF structure: failed to parse PDF document.");
        }

        var rawBtBlocks = ExtractRawBtEtBlocks(rawText);
        var pages = new List<string>();
        bool anyContentExtracted = false;

        using (document)
        {
            for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
            {
                var page = document.GetPage(pageNum);
                var pageSb = new StringBuilder();
                var pageLines = new List<string>();

                try
                {
                    var words = page.GetWords()?.ToList();
                    if (words != null && words.Count > 0)
                    {
                        var sortedWords = words
                            .OrderByDescending(w => w.BoundingBox.Bottom)
                            .ThenBy(w => w.BoundingBox.Left)
                            .ToList();

                        var lines = new List<List<Word>>();
                        foreach (var w in sortedWords)
                        {
                            List<Word>? targetLine = null;
                            foreach (var line in lines)
                            {
                                if (Math.Abs(w.BoundingBox.Bottom - line[0].BoundingBox.Bottom) < 3.0)
                                {
                                    targetLine = line;
                                    break;
                                }
                            }

                            if (targetLine != null)
                            {
                                targetLine.Add(w);
                            }
                            else
                            {
                                lines.Add(new List<Word> { w });
                            }
                        }

                        foreach (var line in lines)
                        {
                            var lineText = string.Join(" ", line.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)).Trim();
                            if (!string.IsNullOrWhiteSpace(lineText))
                            {
                                pageLines.Add(lineText);
                            }
                        }
                    }
                }
                catch
                {
                    // Fall back if GetWords fails (e.g. minimal synthetic test PDF lacking /Font dictionary)
                }

                if (pageLines.Count == 0)
                {
                    try
                    {
                        var pt = page.Text;
                        if (!string.IsNullOrWhiteSpace(pt))
                        {
                            var rawLines = pt.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var l in rawLines)
                            {
                                var trimmed = l.Trim();
                                if (!string.IsNullOrWhiteSpace(trimmed))
                                {
                                    pageLines.Add(trimmed);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Fall back if page.Text fails
                    }
                }

                if (pageLines.Count == 0 && rawBtBlocks.Count > 0)
                {
                    if (pageNum - 1 < rawBtBlocks.Count)
                    {
                        pageLines.Add(rawBtBlocks[pageNum - 1]);
                    }
                    else if (rawBtBlocks.Count == 1 && pageNum == 1)
                    {
                        pageLines.Add(rawBtBlocks[0]);
                    }
                }

                foreach (var line in pageLines)
                {
                    pageSb.AppendLine($"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>");
                    anyContentExtracted = true;
                }

                try
                {
                    var images = page.GetImages();
                    if (images != null)
                    {
                        foreach (var img in images)
                        {
                            if (img.TryGetPng(out var pngBytes) && pngBytes != null && pngBytes.Length > 0)
                            {
                                var base64 = Convert.ToBase64String(pngBytes);
                                pageSb.AppendLine($"<img src=\"data:image/png;base64,{base64}\" style=\"max-width:100%;height:auto;display:block;margin:1rem auto;\" />");
                                anyContentExtracted = true;
                            }
                            else
                            {
                                var raw = img.RawBytes;
                                if (raw.Count >= 2 && raw[0] == 0xFF && raw[1] == 0xD8)
                                {
                                    byte[] rawArray = raw is byte[] bArr ? bArr : raw.ToArray();
                                    var base64 = Convert.ToBase64String(rawArray);
                                    pageSb.AppendLine($"<img src=\"data:image/jpeg;base64,{base64}\" style=\"max-width:100%;height:auto;display:block;margin:1rem auto;\" />");
                                    anyContentExtracted = true;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore image extraction errors
                }

                var pageContent = pageSb.ToString().Trim();
                if (string.IsNullOrWhiteSpace(pageContent))
                {
                    pageContent = $"<p>[PDF Page {pageNum}]</p>";
                }
                pages.Add(pageContent);
            }
        }

        bool hasStream = rawText.Contains("stream", StringComparison.OrdinalIgnoreCase);
        if (anyContentExtracted || hasStream)
        {
            return ServiceResult<ParsedEbook>.Success(new ParsedEbook("PDF", contentHash, pages));
        }

        var fallbackPages = ExtractLegacyBtEtPages(utf8Text);
        if (fallbackPages.Count > 0)
        {
            return ServiceResult<ParsedEbook>.Success(new ParsedEbook("PDF", contentHash, fallbackPages));
        }

        return ServiceResult<ParsedEbook>.Failure(
            400,
            EbookErrorCodes.EmptyPdfContent,
            "The PDF file does not contain readable pages or images.");
    }

    private static List<string> ExtractLegacyBtEtPages(string pdfText)
    {
        var pages = new List<string>();

        var btEtMatches = Regex.Matches(pdfText, @"BT\s*(.*?)\s*ET", RegexOptions.Singleline);
        foreach (Match blockMatch in btEtMatches)
        {
            var blockContent = blockMatch.Groups[1].Value;
            var tjMatches = Regex.Matches(blockContent, @"\((?<txt>(?:\\.|[^\\)])*)\)\s*Tj", RegexOptions.Singleline);
            if (tjMatches.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (Match tj in tjMatches)
                {
                    var unescaped = tj.Groups["txt"].Value
                        .Replace(@"\(", "(")
                        .Replace(@"\)", ")")
                        .Replace(@"\n", "\n")
                        .Replace(@"\\", @"\");
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(unescaped);
                }
                var pageText = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    pages.Add(pageText);
                }
            }
        }

        return pages;
    }

    private static List<string> ExtractRawBtEtBlocks(string rawPdfText)
    {
        var blocks = new List<string>();
        var btMatches = Regex.Matches(rawPdfText, @"BT\s*(.*?)\s*ET", RegexOptions.Singleline);
        foreach (Match m in btMatches)
        {
            var content = m.Groups[1].Value;
            var tjMatches = Regex.Matches(content, @"\((?<txt>(?:\\.|[^\\)])*)\)", RegexOptions.Singleline);
            if (tjMatches.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (Match tj in tjMatches)
                {
                    var txt = tj.Groups["txt"].Value
                        .Replace(@"\(", "(")
                        .Replace(@"\)", ")")
                        .Replace(@"\n", "\n")
                        .Replace(@"\r", "\r")
                        .Replace(@"\t", "\t")
                        .Replace(@"\\", @"\");
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(txt);
                }
                var result = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(result))
                {
                    blocks.Add(result);
                }
            }
        }
        return blocks;
    }

    private static ServiceResult<ParsedEbook> ParseAndValidateEpub(byte[] fileBytes, string contentHash)
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
            using var ms = new MemoryStream(fileBytes, writable: false);
            EpubBook? epubBook = null;
            try
            {
                epubBook = EpubReader.ReadBook(ms);
            }
            catch
            {
                epubBook = null;
            }

            if (epubBook != null && epubBook.ReadingOrder != null && epubBook.ReadingOrder.Count > 0)
            {
                var cssSb = new StringBuilder();
                if (epubBook.Content?.Css?.Local != null)
                {
                    foreach (var cssFile in epubBook.Content.Css.Local)
                    {
                        if (cssFile != null && !string.IsNullOrWhiteSpace(cssFile.Content))
                        {
                            cssSb.AppendLine(cssFile.Content);
                        }
                    }
                }
                var combinedCss = cssSb.ToString().Trim();

                var imageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (epubBook.Content?.Images?.Local != null)
                {
                    foreach (var imgFile in epubBook.Content.Images.Local)
                    {
                        if (imgFile?.Content != null && imgFile.Content.Length > 0)
                        {
                            var mime = !string.IsNullOrWhiteSpace(imgFile.ContentMimeType)
                                ? imgFile.ContentMimeType
                                : "image/jpeg";
                            var dataUri = $"data:{mime};base64,{Convert.ToBase64String(imgFile.Content)}";

                            if (!string.IsNullOrWhiteSpace(imgFile.FilePath))
                            {
                                imageMap[imgFile.FilePath] = dataUri;
                                imageMap[imgFile.FilePath.Replace('\\', '/').TrimStart('/')] = dataUri;
                                imageMap[Path.GetFileName(imgFile.FilePath)] = dataUri;
                            }
                            if (!string.IsNullOrWhiteSpace(imgFile.Key))
                            {
                                imageMap[imgFile.Key] = dataUri;
                            }
                        }
                    }
                }

                var pages = new List<string>();
                foreach (var chapter in epubBook.ReadingOrder)
                {
                    if (chapter != null && !string.IsNullOrWhiteSpace(chapter.Content))
                    {
                        var chapterHtml = FormatChapterHtml(chapter.Content, imageMap, combinedCss);
                        if (!string.IsNullOrWhiteSpace(chapterHtml))
                        {
                            pages.Add(chapterHtml);
                        }
                    }
                }

                if (pages.Count > 0)
                {
                    return ServiceResult<ParsedEbook>.Success(new ParsedEbook("EPUB", contentHash, pages));
                }
            }

            return ParseEpubZipFallback(fileBytes, contentHash);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.CorruptedEpubArchive,
                $"Corrupted EPUB archive: {ex.Message}");
        }
    }

    private static ServiceResult<ParsedEbook> ParseEpubZipFallback(byte[] fileBytes, string contentHash)
    {
        try
        {
            using var ms = new MemoryStream(fileBytes, writable: false);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);

            if (archive.Entries.Count == 0)
            {
                return ServiceResult<ParsedEbook>.Failure(
                    400,
                    EbookErrorCodes.EmptyEpubArchive,
                    "Corrupted EPUB file: the archive is empty.");
            }

            bool hasContainerOrOpf = archive.Entries.Any(e =>
                e.FullName.Equals("META-INF/container.xml", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith(".opf", StringComparison.OrdinalIgnoreCase));

            var contentEntries = archive.Entries
                .Where(e =>
                    e.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                    e.FullName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!hasContainerOrOpf || contentEntries.Count == 0)
            {
                return ServiceResult<ParsedEbook>.Failure(
                    400,
                    EbookErrorCodes.CorruptedEpubStructure,
                    "Corrupted EPUB structure: missing container.xml/.opf metadata or readable XHTML chapter.");
            }

            foreach (var entry in archive.Entries)
            {
                using var entryStream = entry.Open();
                var buffer = new byte[1024];
                while (entryStream.Read(buffer, 0, buffer.Length) > 0)
                {
                }
            }

            var zipImageMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var imageEntries = archive.Entries.Where(e =>
                e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            foreach (var imgEntry in imageEntries)
            {
                using var imgStream = imgEntry.Open();
                using var imgMs = new MemoryStream();
                imgStream.CopyTo(imgMs);
                var imgBytes = imgMs.ToArray();
                var ext = Path.GetExtension(imgEntry.FullName).ToLowerInvariant();
                var mime = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    ".svg" => "image/svg+xml",
                    _ => "image/jpeg"
                };
                var dataUri = $"data:{mime};base64,{Convert.ToBase64String(imgBytes)}";
                zipImageMap[imgEntry.FullName] = dataUri;
                zipImageMap[imgEntry.FullName.Replace('\\', '/').TrimStart('/')] = dataUri;
                zipImageMap[Path.GetFileName(imgEntry.FullName)] = dataUri;
            }

            var zipCssSb = new StringBuilder();
            var cssEntries = archive.Entries.Where(e => e.FullName.EndsWith(".css", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var cssEntry in cssEntries)
            {
                using var cssStream = cssEntry.Open();
                using var reader = new StreamReader(cssStream, Encoding.UTF8);
                var cssText = reader.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(cssText))
                {
                    zipCssSb.AppendLine(cssText);
                }
            }
            var zipCombinedCss = zipCssSb.ToString().Trim();

            var pages = new List<string>();
            foreach (var entry in contentEntries)
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var html = reader.ReadToEnd();
                var chapterHtml = FormatChapterHtml(html, zipImageMap, zipCombinedCss);
                if (!string.IsNullOrWhiteSpace(chapterHtml))
                {
                    pages.Add(chapterHtml);
                }
            }

            if (pages.Count == 0)
            {
                return ServiceResult<ParsedEbook>.Failure(
                    400,
                    EbookErrorCodes.EmptyEpubContent,
                    "The EPUB file does not contain readable text chapters.");
            }

            return ServiceResult<ParsedEbook>.Success(new ParsedEbook("EPUB", contentHash, pages));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.CorruptedEpubArchive,
                $"Corrupted EPUB archive: {ex.Message}");
        }
    }

    private static string FormatChapterHtml(string rawHtml, Dictionary<string, string> imageMap, string combinedCss = "")
    {
        if (string.IsNullOrWhiteSpace(rawHtml))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(rawHtml, @"<script[^>]*>.*?</script>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        var existingStyles = new StringBuilder();
        var styleMatches = Regex.Matches(cleaned, @"<style[^>]*>.*?</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match sm in styleMatches)
        {
            existingStyles.AppendLine(sm.Value);
        }

        var bodyMatch = Regex.Match(cleaned, @"<body[^>]*>(.*?)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var contentHtml = bodyMatch.Success ? bodyMatch.Groups[1].Value.Trim() : cleaned.Trim();

        if (string.IsNullOrWhiteSpace(contentHtml))
        {
            return string.Empty;
        }

        contentHtml = Regex.Replace(
            contentHtml,
            @"<p[^>]*>(?:\s|&nbsp;|&#160;|<br\s*/?>)*</p>",
            "<div class=\"epub-spacer\"></div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var textOnly = Regex.Replace(contentHtml, @"<[^>]+>", string.Empty);
        bool hasVisibleText = !string.IsNullOrWhiteSpace(System.Net.WebUtility.HtmlDecode(textOnly));
        bool hasImg = Regex.IsMatch(contentHtml, @"<(img|image)\b", RegexOptions.IgnoreCase);
        if (!hasVisibleText && !hasImg)
        {
            return string.Empty;
        }

        contentHtml = Regex.Replace(
            contentHtml,
            @"(?<tag><(?:img|image)\b[^>]*?\b(?:src|href|xlink:href)\s*=\s*[""'])(?<url>[^""']+)(?<rest>[""'][^>]*>)",
            match =>
            {
                var url = match.Groups["url"].Value;
                var cleanUrl = url.Replace('\\', '/').TrimStart('/');
                var fileName = Path.GetFileName(cleanUrl);

                if (imageMap.TryGetValue(url, out var dataUri) ||
                    imageMap.TryGetValue(cleanUrl, out dataUri) ||
                    imageMap.TryGetValue(fileName, out dataUri))
                {
                    return $"{match.Groups["tag"].Value}{dataUri}{match.Groups["rest"].Value}";
                }

                return match.Value;
            },
            RegexOptions.IgnoreCase);

        const string baseStyle = "<style>body{margin:0;font-family:Georgia,serif;line-height:1.65;color:#0f172a;} p{margin:0.25em 0;} .epub-spacer{height:0.6em;} img,svg{max-width:100%;height:auto;display:block;margin:0.75rem auto;}</style>";

        var styleHeader = new StringBuilder();
        styleHeader.AppendLine(baseStyle);
        if (!string.IsNullOrWhiteSpace(combinedCss))
        {
            styleHeader.AppendLine($"<style>{combinedCss}</style>");
        }
        if (existingStyles.Length > 0 && !contentHtml.Contains("<style", StringComparison.OrdinalIgnoreCase))
        {
            styleHeader.Append(existingStyles);
        }

        return $"{styleHeader.ToString().Trim()}\n{contentHtml}".Trim();
    }
}
