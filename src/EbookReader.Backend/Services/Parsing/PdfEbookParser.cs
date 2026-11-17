namespace EbookReader.Backend.Services;

using System.Text;
using EbookReader.Backend.Models;
using UglyToad.PdfPig;

public static class PdfEbookParser
{
    public static ServiceResult<ParsedEbook> ParseAndValidate(byte[] fileBytes, string contentHash)
    {
        if (fileBytes.Length < 16)
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.CorruptedPdf, "Corrupted PDF file: the file is too short.");
        }

        var rawText = Encoding.Latin1.GetString(fileBytes);

        if (!rawText.StartsWith("%PDF-", StringComparison.Ordinal))
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.CorruptedPdfHeader, "Corrupted PDF file: missing '%PDF-' header.");
        }

        var tailStart = Math.Max(0, rawText.Length - 1024);
        var tail = rawText[tailStart..];
        if (!tail.Contains("%%EOF", StringComparison.Ordinal))
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.CorruptedPdfEof, "Corrupted PDF structure: missing '%%EOF' marker.");
        }

        bool hasPageObject = rawText.Contains("/Type /Page", StringComparison.OrdinalIgnoreCase)
                             || rawText.Contains("/Type/Page", StringComparison.OrdinalIgnoreCase)
                             || rawText.Contains("/Pages", StringComparison.OrdinalIgnoreCase);

        if (!hasPageObject)
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.CorruptedPdfStructure, "Corrupted PDF structure: no valid page structure (/Page) found.");
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
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.CorruptedPdfStructure, "Corrupted PDF structure: failed to parse PDF document.");
        }

        var pages = new List<string>();
        var pdfToc = new List<TocItem>();
        bool anyContentExtracted = false;

        string? metadataTitle = !string.IsNullOrWhiteSpace(document.Information?.Title)
            ? document.Information.Title.Trim()
            : null;
        string? metadataAuthor = !string.IsNullOrWhiteSpace(document.Information?.Author)
            ? document.Information.Author.Trim()
            : null;

        using (document)
        {
            for (int pageNum = 1; pageNum <= document.NumberOfPages; pageNum++)
            {
                var page = document.GetPage(pageNum);
                var pageContent = PdfPageContentExtractor.ExtractPageHtml(page, pageNum, ref anyContentExtracted);
                pages.Add(pageContent);
            }

            try
            {
                if (document.TryGetBookmarks(out var pdfBookmarks) && pdfBookmarks?.Roots != null)
                {
                    TraversePdfBookmarks(pdfBookmarks.Roots, 0, document.NumberOfPages, pdfToc);
                }
            }
            catch
            {
                // Ignore outline extraction error
            }
        }

        if (!anyContentExtracted || pages.Count == 0 || pages.All(string.IsNullOrWhiteSpace))
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.EmptyPdfContent, "The PDF file does not contain readable pages or images.");
        }

        return ServiceResult<ParsedEbook>.Success(new ParsedEbook("PDF", contentHash, pages, metadataTitle, metadataAuthor, pdfToc));
    }

    private static void TraversePdfBookmarks(IEnumerable<UglyToad.PdfPig.Outline.BookmarkNode> nodes, int level, int maxPages, List<TocItem> toc)
    {
        foreach (var node in nodes)
        {
            if (node == null) continue;
            int pageNum = node is UglyToad.PdfPig.Outline.DocumentBookmarkNode docNode ? docNode.PageNumber : 1;

            if (!string.IsNullOrWhiteSpace(node.Title))
            {
                toc.Add(new TocItem
                {
                    Title = node.Title.Trim(),
                    PageNumber = Math.Clamp(pageNum, 1, Math.Max(1, maxPages)),
                    Level = level
                });
            }

            if (node.Children is { Count: > 0 })
            {
                TraversePdfBookmarks(node.Children, level + 1, maxPages, toc);
            }
        }
    }
}
