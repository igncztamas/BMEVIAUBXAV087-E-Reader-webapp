namespace EbookReader.Backend.Services;

using System.Text;
using System.Text.RegularExpressions;

public static class HtmlPaginator
{
    public static List<string> FormatChapterHtml(string rawHtml, Dictionary<string, string> imageMap, string combinedCss = "")
    {
        if (string.IsNullOrWhiteSpace(rawHtml))
        {
            return new List<string>();
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
            return new List<string>();
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
            return new List<string>();
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

        return PaginateChapterHtml(contentHtml, string.Empty);
    }

    public static List<string> PaginateChapterHtml(string bodyHtml, string styleBlock, int maxTextCharsPerPage = 2600)
    {
        if (string.IsNullOrWhiteSpace(bodyHtml))
        {
            return new List<string>();
        }

        var cleanedBody = Regex.Replace(bodyHtml, @"<style[^>]*>.*?</style>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();

        var textOnly = Regex.Replace(cleanedBody, @"<[^>]+>", string.Empty);
        var visibleText = System.Net.WebUtility.HtmlDecode(textOnly).Trim();

        var prefix = string.IsNullOrWhiteSpace(styleBlock) ? string.Empty : styleBlock.Trim() + "\n";

        if (visibleText.Length <= maxTextCharsPerPage)
        {
            return new List<string> { $"{prefix}{cleanedBody}".Trim() };
        }

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
            int maxChars = (currentHasImage || chunkHasImage) ? 1200 : maxTextCharsPerPage;
            int minCharsBeforeBreak = (currentHasImage || chunkHasImage) ? 800 : 1800;
            int overflowTolerance = (currentHasImage || chunkHasImage) ? 300 : 500;

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

        if (currentChunkSb.Length > 0)
        {
            var pageContent = $"{prefix}{currentChunkSb.ToString().Trim()}".Trim();
            pages.Add(pageContent);
        }

        return pages.Count > 0 ? pages : new List<string> { $"{prefix}{cleanedBody}".Trim() };
    }
}
