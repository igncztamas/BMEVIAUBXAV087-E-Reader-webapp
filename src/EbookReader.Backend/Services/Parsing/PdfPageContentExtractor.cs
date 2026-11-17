namespace EbookReader.Backend.Services;

using System.Text;
using UglyToad.PdfPig.Content;

public static class PdfPageContentExtractor
{
    public static string ExtractPageHtml(Page page, int pageNum, ref bool anyContentExtracted)
    {
        var pageSb = new StringBuilder();
        var pageLines = ExtractPageLines(page);

        foreach (var line in pageLines)
        {
            pageSb.AppendLine($"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>");
            anyContentExtracted = true;
        }

        if (AppendPageImages(page, pageSb))
        {
            anyContentExtracted = true;
        }

        var content = pageSb.ToString().Trim();
        return string.IsNullOrWhiteSpace(content) ? $"<p>[PDF Page {pageNum}]</p>" : content;
    }

    public static List<string> ExtractPageLines(Page page)
    {
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
            // Ignore word extraction errors and fall back to page.Text
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

        return pageLines;
    }

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
                    if (img.TryGetPng(out var pngBytes) && pngBytes is { Length: > 0 })
                    {
                        var base64 = Convert.ToBase64String(pngBytes);
                        pageSb.AppendLine($"<img src=\"data:image/png;base64,{base64}\" style=\"max-width:100%;height:auto;display:block;margin:1rem auto;\" />");
                        imageFound = true;
                    }
                    else
                    {
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
