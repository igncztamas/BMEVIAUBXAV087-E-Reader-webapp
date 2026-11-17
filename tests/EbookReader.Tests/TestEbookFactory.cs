using System.IO.Compression;
using System.Net;
using System.Text;

namespace EbookReader.Tests;

/// <summary>
/// Generates standard-compliant in-memory PDF, EPUB, and ZIP payloads for automated testing.
/// </summary>
public static class TestEbookFactory
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static byte[] CreateValidPdfBytes(params string[] pages)
        => CreateValidPdfBytesWithMetadata(null, null, pages);

    public static byte[] CreateValidPdfBytesWithMetadata(string? title, string? author, params string[] pages)
    {
        if (pages is null || pages.Length == 0)
        {
            pages = ["Page 1: Welcome to the E-book Reader!"];
        }

        using var ms = new MemoryStream();
        var offsets = new List<long>();

        void WriteAscii(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            ms.Write(bytes, 0, bytes.Length);
        }

        void WriteObject(int id, string body)
        {
            offsets.Add(ms.Position);
            WriteAscii($"{id} 0 obj\n{body}\nendobj\n");
        }

        WriteAscii("%PDF-1.4\n");

        var kidsRefs = string.Join(" ", Enumerable.Range(0, pages.Length).Select(i => $"{4 + i * 2} 0 R"));
        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(2, $"<< /Type /Pages /Count {pages.Length} /Kids [{kidsRefs}] >>");
        WriteObject(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        for (int i = 0; i < pages.Length; i++)
        {
            int pageObjId = 4 + i * 2;
            int contentObjId = pageObjId + 1;
            var streamContent = $"BT /F1 12 Tf 72 720 Td ({EscapePdfText(pages[i])}) Tj ET";
            var streamLength = Encoding.ASCII.GetByteCount(streamContent);

            WriteObject(pageObjId, $"<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 3 0 R >> >> /MediaBox [0 0 612 792] /Contents {contentObjId} 0 R >>");
            WriteObject(contentObjId, $"<< /Length {streamLength} >>\nstream\n{streamContent}\nendstream");
        }

        string infoRef = string.Empty;
        if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(author))
        {
            int infoObjId = 4 + pages.Length * 2;
            var infoEntries = new List<string>();
            if (!string.IsNullOrWhiteSpace(title))
            {
                infoEntries.Add($"/Title ({EscapePdfText(title)})");
            }
            if (!string.IsNullOrWhiteSpace(author))
            {
                infoEntries.Add($"/Author ({EscapePdfText(author)})");
            }
            WriteObject(infoObjId, $"<< {string.Join(" ", infoEntries)} >>");
            infoRef = $" /Info {infoObjId} 0 R";
        }

        long xrefOffset = ms.Position;
        int totalObjects = offsets.Count + 1;
        WriteAscii($"xref\n0 {totalObjects}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            WriteAscii($"{offset:D10} 00000 n \n");
        }

        WriteAscii($"trailer\n<< /Size {totalObjects} /Root 1 0 R{infoRef} >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return ms.ToArray();
    }

    public static byte[] CreateValidEpubBytes(string title, params string[] chapters)
        => CreateValidEpubBytesWithMetadata(title, null, null, chapters);

    public static byte[] CreateValidEpubBytesWithMetadata(
        string? title,
        string? author,
        string[]? tocTitles,
        params string[] chapters)
    {
        if (chapters is null || chapters.Length == 0)
        {
            chapters = ["Chapter 1: Introduction to EPUB reading."];
        }

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "mimetype", "application/epub+zip", Encoding.ASCII, CompressionLevel.NoCompression);
            WriteEntry(archive, "META-INF/container.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                  </rootfiles>
                </container>
                """, Utf8NoBom);

            WriteEpubPackageAndContent(archive, title, author, tocTitles, chapters);
        }

        return ms.ToArray();
    }

    public static byte[] CreateZipBytes(string entryName, string content)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, entryName, content, Utf8NoBom);
        }
        return ms.ToArray();
    }

    private static void WriteEpubPackageAndContent(
        ZipArchive archive,
        string? title,
        string? author,
        string[]? tocTitles,
        string[] chapters)
    {
        var manifestItems = string.Join("\n    ", Enumerable.Range(1, chapters.Length)
            .Select(i => $"""<item id="ch{i}" href="chapter_{i:D2}.xhtml" media-type="application/xhtml+xml"/>"""));
        var spineItems = string.Join("\n    ", Enumerable.Range(1, chapters.Length)
            .Select(i => $"""<itemref idref="ch{i}"/>"""));
        var navItems = string.Join("\n        ", Enumerable.Range(1, chapters.Length)
            .Select(i =>
            {
                var label = tocTitles is not null && i - 1 < tocTitles.Length ? tocTitles[i - 1] : $"Chapter {i}";
                return $"""<li><a href="chapter_{i:D2}.xhtml">{WebUtility.HtmlEncode(label)}</a></li>""";
            }));

        var titleElement = string.IsNullOrWhiteSpace(title)
            ? string.Empty
            : $"\n    <dc:title>{WebUtility.HtmlEncode(title)}</dc:title>";
        var authorElement = string.IsNullOrWhiteSpace(author)
            ? string.Empty
            : $"\n    <dc:creator>{WebUtility.HtmlEncode(author)}</dc:creator>";

        WriteEntry(archive, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="bookid">urn:uuid:{Guid.NewGuid()}</dc:identifier>{titleElement}{authorElement}
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                {manifestItems}
              </manifest>
              <spine>
                {spineItems}
              </spine>
            </package>
            """, Utf8NoBom);

        WriteEntry(archive, "OEBPS/nav.xhtml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <head><title>Navigation</title></head>
              <body>
                <nav epub:type="toc">
                  <ol>
                    {navItems}
                  </ol>
                </nav>
              </body>
            </html>
            """, Utf8NoBom);

        var pageTitle = string.IsNullOrWhiteSpace(title) ? "Book" : title;
        for (int i = 0; i < chapters.Length; i++)
        {
            WriteEntry(archive, $"OEBPS/chapter_{i + 1:D2}.xhtml", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <head><title>{WebUtility.HtmlEncode(pageTitle)} - Page {i + 1}</title></head>
                  <body>
                    <p>{WebUtility.HtmlEncode(chapters[i])}</p>
                  </body>
                </html>
                """, Utf8NoBom);
        }
    }

    private static string EscapePdfText(string text)
        => text.Replace(@"\", @"\\").Replace("(", @"\(").Replace(")", @"\)");

    private static void WriteEntry(
        ZipArchive archive,
        string entryName,
        string content,
        Encoding encoding,
        CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        var entry = archive.CreateEntry(entryName, compressionLevel);
        using var writer = new StreamWriter(entry.Open(), encoding);
        writer.Write(content);
    }
}
