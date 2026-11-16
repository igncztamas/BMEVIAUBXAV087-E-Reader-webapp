using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EbookReader.Backend.Models;

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

        var pages = ExtractPdfPages(utf8Text);
        if (pages.Count == 0)
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.EmptyPdfContent,
                "The PDF file does not contain readable pages.");
        }

        return ServiceResult<ParsedEbook>.Success(new ParsedEbook("PDF", contentHash, pages));
    }

    private static List<string> ExtractPdfPages(string pdfText)
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

        if (pages.Count > 0)
        {
            return pages;
        }

        var pageObjectCount = Regex.Matches(pdfText, @"/Type\s*/Page\b", RegexOptions.IgnoreCase).Count;
        if (pageObjectCount > 0)
        {
            for (int i = 1; i <= pageObjectCount; i++)
            {
                pages.Add($"[PDF Page {i} content]");
            }
        }

        return pages;
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

            var pages = new List<string>();
            foreach (var entry in contentEntries)
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var html = reader.ReadToEnd();
                var plainText = ExtractPlainTextFromHtml(html);
                if (!string.IsNullOrWhiteSpace(plainText))
                {
                    pages.Add(plainText);
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

    private static string ExtractPlainTextFromHtml(string html)
    {
        var cleaned = Regex.Replace(html, @"<(head|style|script)[^>]*>.*?</\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"</?(p|div|br|h[1-6]|li|tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"<[^>]+>", string.Empty);
        cleaned = System.Net.WebUtility.HtmlDecode(cleaned);
        var lines = cleaned
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l));
        return string.Join("\n\n", lines);
    }
}
