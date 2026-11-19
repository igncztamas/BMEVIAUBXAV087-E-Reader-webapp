namespace EbookReader.Backend.Services;

using System.Text.RegularExpressions;
using EbookReader.Backend.Models;
using VersOne.Epub;

/// <summary>
/// Helper extracting Table of Contents hierarchies from EPUB navigation items and heading fallbacks.
/// </summary>
public static class EpubTocExtractor
{
    /// <summary>
    /// Recursively builds a Table of Contents list from EPUB navigation hierarchy.
    /// </summary>
    public static void BuildNavigationToc(
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

    /// <summary>
    /// Resolves the target page number corresponding to a content file path and optional HTML anchor.
    /// </summary>
    public static int ResolvePageForPath(
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

        // Match anchor ID or name attribute across page range
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

    /// <summary>
    /// Fallback TOC generator that scans h1-h3 heading tags in chapter pages.
    /// </summary>
    public static void BuildHeadingToc(
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
