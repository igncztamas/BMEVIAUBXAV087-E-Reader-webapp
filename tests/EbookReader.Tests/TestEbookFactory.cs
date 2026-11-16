using System.IO.Compression;
using System.Text;

namespace EbookReader.Tests;

public static class TestEbookFactory
{
    public static byte[] CreateValidPdfBytes(params string[] pages)
    {
        if (pages is null || pages.Length == 0)
        {
            pages = ["Page 1: Welcome to the E-book Reader!"];
        }

        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.4");
        sb.AppendLine("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj");

        var kidsRefs = string.Join(" ", Enumerable.Range(0, pages.Length).Select(i => $"{3 + i * 2} 0 R"));
        sb.AppendLine($"2 0 obj << /Type /Pages /Count {pages.Length} /Kids [{kidsRefs}] >> endobj");

        for (int i = 0; i < pages.Length; i++)
        {
            int pageObjId = 3 + i * 2;
            int contentObjId = pageObjId + 1;
            var escapedText = pages[i]
                .Replace(@"\", @"\\")
                .Replace("(", @"\(")
                .Replace(")", @"\)");

            var streamContent = $"BT /F1 12 Tf 72 720 Td ({escapedText}) Tj ET";
            sb.AppendLine($"{pageObjId} 0 obj << /Type /Page /Parent 2 0 R /Contents {contentObjId} 0 R >> endobj");
            sb.AppendLine($"{contentObjId} 0 obj << /Length {streamContent.Length} >> stream");
            sb.AppendLine(streamContent);
            sb.AppendLine("endstream endobj");
        }

        sb.AppendLine("trailer << /Root 1 0 R >>");
        sb.AppendLine("%%EOF");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] CreateValidEpubBytes(string title, params string[] chapters)
    {
        if (chapters is null || chapters.Length == 0)
        {
            chapters = ["Chapter 1: Introduction to EPUB reading."];
        }

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mimeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(mimeEntry.Open(), Encoding.ASCII))
            {
                writer.Write("application/epub+zip");
            }

            var containerEntry = archive.CreateEntry("META-INF/container.xml");
            using (var writer = new StreamWriter(containerEntry.Open(), Encoding.UTF8))
            {
                writer.Write("""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                      <rootfiles>
                        <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                      </rootfiles>
                    </container>
                    """);
            }

            var opfEntry = archive.CreateEntry("OEBPS/content.opf");
            using (var writer = new StreamWriter(opfEntry.Open(), Encoding.UTF8))
            {
                writer.Write($"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                      <metadata><dc:title xmlns:dc="http://purl.org/dc/elements/1.1/">{System.Net.WebUtility.HtmlEncode(title)}</dc:title></metadata>
                    </package>
                    """);
            }

            for (int i = 0; i < chapters.Length; i++)
            {
                var chapterEntry = archive.CreateEntry($"OEBPS/chapter_{i + 1:D2}.xhtml");
                using var writer = new StreamWriter(chapterEntry.Open(), Encoding.UTF8);
                writer.Write($"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <html xmlns="http://www.w3.org/1999/xhtml">
                      <head><title>{System.Net.WebUtility.HtmlEncode(title)} - Page {i + 1}</title></head>
                      <body>
                        <p>{System.Net.WebUtility.HtmlEncode(chapters[i])}</p>
                      </body>
                    </html>
                    """);
            }
        }

        return ms.ToArray();
    }
}
