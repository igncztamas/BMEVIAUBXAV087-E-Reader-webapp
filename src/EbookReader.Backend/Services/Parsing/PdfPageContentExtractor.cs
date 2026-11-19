namespace EbookReader.Backend.Services;

using System.Text;
using UglyToad.PdfPig.Content;

/// <summary>
/// Helper extracting formatted HTML, text line grouping, and embedded raster images from individual PDF pages.
/// </summary>
public static class PdfPageContentExtractor
{
    /// <summary>
    /// Extracts HTML markup for a single PDF page, including grouped text lines and embedded images.
    /// </summary>
    /// <param name="page">The PdfPig page object.</param>
    /// <param name="pageNum">The 1-based page number.</param>
    /// <param name="anyContentExtracted">Reference flag set to true if any text or image content was extracted.</param>
    /// <returns>HTML page content string.</returns>
    public static string ExtractPageHtml(Page page, int pageNum, ref bool anyContentExtracted)
    {
        var pageSb = new StringBuilder();
        var pageLines = ExtractPageLines(page);

        // Append HTML paragraph tags for each line of text
        foreach (var line in pageLines)
        {
            pageSb.AppendLine($"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>");
            anyContentExtracted = true;
        }

        // Append embedded raster images if found
        if (AppendPageImages(page, pageSb))
        {
            anyContentExtracted = true;
        }

        var content = pageSb.ToString().Trim();
        return string.IsNullOrWhiteSpace(content) ? $"<p>[PDF Page {pageNum}]</p>" : content;
    }

    /// <summary>
    /// Groups words into visual lines by comparing vertical bounding box coordinates.
    /// </summary>
    /// <param name="page">The PdfPig page object.</param>
    /// <returns>A list of ordered text lines.</returns>
    public static List<string> ExtractPageLines(Page page)
    {
        var pageLines = new List<string>();

        try
        {
            var words = page.GetWords()?.ToList();
            if (words != null && words.Count > 0)
            {
                // Sort words top-to-bottom, left-to-right
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
                        // Cluster words within 3pt vertical baseline proximity into the same line
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

                // Format each clustered line ordered by horizontal left coordinate
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
            // Ignore word extraction errors and fall back to raw page.Text
        }

        // Fallback to raw page.Text if word clustering yielded nothing
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

        return pageLines;
    }

    /// <summary>
    /// Extracts PNG and JPEG images from the page and appends them as Base64 data URIs.
    /// </summary>
    /// <param name="page">The PdfPig page object.</param>
    /// <param name="pageSb">The string builder accumulating page HTML.</param>
    /// <returns>True if at least one image was appended; otherwise false.</returns>
    public static bool AppendPageImages(Page page, StringBuilder pageSb)
    {
        bool imageFound = false;
        try
        {
            var images = page.GetImages();
            if (images != null)
            {
                foreach (var img in images)
                {
                    // Check for lossless PNG conversion
                    if (img.TryGetPng(out var pngBytes) && pngBytes is { Length: > 0 })
                    {
                        var base64 = Convert.ToBase64String(pngBytes);
                        pageSb.AppendLine($"<img src=\"data:image/png;base64,{base64}\" style=\"max-width:100%;height:auto;display:block;margin:1rem auto;\" />");
                        imageFound = true;
                    }
                    else
                    {
                        // Check for raw JPEG byte signature (0xFF, 0xD8)
                        var raw = img.RawBytes;
                        if (raw.Count >= 2 && raw[0] == 0xFF && raw[1] == 0xD8)
                        {
                            byte[] rawArray = raw is byte[] bArr ? bArr : raw.ToArray();
                            var base64 = Convert.ToBase64String(rawArray);
                            pageSb.AppendLine($"<img src=\"data:image/jpeg;base64,{base64}\" style=\"max-width:100%;height:auto;display:block;margin:1rem auto;\" />");
                            imageFound = true;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore image extraction errors
        }

        return imageFound;
    }
}
