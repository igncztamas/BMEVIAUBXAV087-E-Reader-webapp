namespace EbookReader.Backend.Services;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Utility class for cleaning, preparing, and paginating chapter HTML content into reading pages.
/// </summary>
public static class HtmlPaginator
{
    /// <summary>
    /// Cleans chapter HTML, strips scripts, resolves image URIs from map, and paginates body content.
    /// </summary>
    /// <param name="rawHtml">The raw HTML string of the chapter.</param>
    /// <param name="imageMap">Dictionary mapping original image relative paths to base64 data URIs.</param>
    /// <param name="combinedCss">Optional combined stylesheet to prefix onto pages.</param>
    /// <returns>A list of paginated HTML page strings.</returns>
    public static List<string> FormatChapterHtml(string rawHtml, Dictionary<string, string> imageMap, string combinedCss = "")
    {
        if (string.IsNullOrWhiteSpace(rawHtml))
        {
            return new List<string>();
        }

        // Strip executable script tags for security
        var cleaned = Regex.Replace(rawHtml, @"<script[^>]*>.*?</script>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Extract any inline style blocks
        var existingStyles = new StringBuilder();
        var styleMatches = Regex.Matches(cleaned, @"<style[^>]*>.*?</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match sm in styleMatches)
        {
            existingStyles.AppendLine(sm.Value);
        }

        // Extract body tag contents or fall back to full cleaned markup
        var bodyMatch = Regex.Match(cleaned, @"<body[^>]*>(.*?)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var contentHtml = bodyMatch.Success ? bodyMatch.Groups[1].Value.Trim() : cleaned.Trim();

        if (string.IsNullOrWhiteSpace(contentHtml))
        {
            return new List<string>();
        }

        // Normalize empty spacer paragraphs
        contentHtml = Regex.Replace(
            contentHtml,
            @"<p[^>]*>(?:\s|&nbsp;|&#160;|<br\s*/?>)*</p>",
            "<div class=\"epub-spacer\"></div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Ensure chapter has visible text or an image
        var textOnly = Regex.Replace(contentHtml, @"<[^>]+>", string.Empty);
        bool hasVisibleText = !string.IsNullOrWhiteSpace(System.Net.WebUtility.HtmlDecode(textOnly));
        bool hasImg = Regex.IsMatch(contentHtml, @"<(img|image)\b", RegexOptions.IgnoreCase);
        if (!hasVisibleText && !hasImg)
        {
            return new List<string>();
        }

        // Replace relative image references with embedded base64 data URIs
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

        return PaginateChapterHtml(contentHtml, string.Empty);
    }

    /// <summary>
    /// Splits an HTML chapter body into page-sized blocks respecting paragraph and heading boundaries.
    /// </summary>
    /// <param name="bodyHtml">The cleaned body HTML.</param>
    /// <param name="styleBlock">Optional style block to prepend to each page.</param>
    /// <param name="maxTextCharsPerPage">Character threshold target per page (default ~2600 chars).</param>
    /// <returns>A list of formatted page HTML strings.</returns>
    public static List<string> PaginateChapterHtml(string bodyHtml, string styleBlock, int maxTextCharsPerPage = 2600)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml))
        {
            return new List<string>();
        }

        // Strip inline styles from body chunk to measure visible text
        var cleanedBody = Regex.Replace(bodyHtml, @"<style[^>]*>.*?</style>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();

        var textOnly = Regex.Replace(cleanedBody, @"<[^>]+>", string.Empty);
        var visibleText = System.Net.WebUtility.HtmlDecode(textOnly).Trim();

        var prefix = string.IsNullOrWhiteSpace(styleBlock) ? string.Empty : styleBlock.Trim() + "\n";

        // If chapter fits on a single page, return immediately
        if (visibleText.Length <= maxTextCharsPerPage)
        {
            return new List<string> { $"{prefix}{cleanedBody}".Trim() };
        }

        // Split HTML cleanly at block-closing tag boundaries
        var rawChunks = Regex.Split(
            cleanedBody,
            @"(?<=(?:</p>|</div>|</h[1-6]>|</blockquote>|</ul>|</ol>|<hr[^>]*>))",
            RegexOptions.IgnoreCase);

        var pages = new List<string>();
        var currentChunkSb = new StringBuilder();
        int currentTextLength = 0;
        bool currentHasImage = false;

        foreach (var chunk in rawChunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            var chunkText = System.Net.WebUtility.HtmlDecode(Regex.Replace(chunk, @"<[^>]+>", string.Empty)).Trim();
            bool chunkHasImage = Regex.IsMatch(chunk, @"<(?:img|image|svg)\b", RegexOptions.IgnoreCase);

            // Dynamically adjust page capacity when an image is present
            int maxChars = (currentHasImage || chunkHasImage) ? 1200 : maxTextCharsPerPage;
            int minCharsBeforeBreak = (currentHasImage || chunkHasImage) ? 800 : 1800;
            int overflowTolerance = (currentHasImage || chunkHasImage) ? 300 : 500;

            // Page break condition
            if (currentChunkSb.Length > 0 && (currentTextLength + chunkText.Length > maxChars))
            {
                if (currentTextLength >= minCharsBeforeBreak || (currentTextLength + chunkText.Length > maxChars + overflowTolerance))
                {
                    var pageContent = $"{prefix}{currentChunkSb.ToString().Trim()}".Trim();
                    pages.Add(pageContent);
                    currentChunkSb.Clear();
                    currentTextLength = 0;
                    currentHasImage = false;
                }
            }

            currentChunkSb.Append(chunk);
            currentTextLength += chunkText.Length;
            if (chunkHasImage)
            {
                currentHasImage = true;
            }
        }

        // Flush remaining chunk to final page
        if (currentChunkSb.Length > 0)
        {
            var pageContent = $"{prefix}{currentChunkSb.ToString().Trim()}".Trim();
            pages.Add(pageContent);
        }

        return pages.Count > 0 ? pages : new List<string> { $"{prefix}{cleanedBody}".Trim() };
    }
}
