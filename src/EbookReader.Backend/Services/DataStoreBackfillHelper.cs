namespace EbookReader.Backend.Services;

using System.Text.RegularExpressions;
using EbookReader.Backend.Models;
using Microsoft.EntityFrameworkCore;

public static class DataStoreBackfillHelper
{
    public static async Task BackfillExistingBooksAsync(List<Book> books, EbookDbContext db)
    {
        foreach (var book in books)
        {
            if (book.FileBytes == null || book.FileBytes.Length == 0) continue;

            bool needsRepagination = NeedsRepagination(book);
            bool missingToc = book.TableOfContents == null || book.TableOfContents.Count == 0;

            if (!needsRepagination && !missingToc) continue;

            var parser = new EbookValidatorAndParser();
            var parseResult = parser.ValidateAndParse(book.FileName, book.FileBytes);
            if (!parseResult.IsSuccess || parseResult.Data == null) continue;

            bool updated = false;
            if (missingToc && parseResult.Data.TableOfContents is { Count: > 0 })
            {
                book.TableOfContents = parseResult.Data.TableOfContents;
                updated = true;
            }

            if (needsRepagination && parseResult.Data.Pages is { Count: > 0 })
            {
                book.Pages = parseResult.Data.Pages;
                book.CurrentPage = Math.Clamp(book.CurrentPage, 1, book.Pages.Count);
                updated = true;
            }

            if (updated)
            {
                await db.Books
                    .Where(b => b.Id == book.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.TableOfContents, book.TableOfContents)
                        .SetProperty(b => b.Pages, book.Pages)
                        .SetProperty(b => b.CurrentPage, book.CurrentPage));
            }
        }
    }

    private static bool NeedsRepagination(Book book)
    {
        if (!string.Equals(book.Format, "EPUB", StringComparison.OrdinalIgnoreCase) ||
            book.Pages == null || book.Pages.Count == 0)
        {
            return false;
        }

        bool hasStyleTag = book.Pages.Any(p => p != null && p.Contains("<style", StringComparison.OrdinalIgnoreCase));
        bool hasOversizedPage = book.Pages.Any(p => p != null && p.Length > 5000);
        bool isOverFragmented = false;

        if (book.Pages.Count > 10)
        {
            double avgVisibleText = book.Pages.Average(p =>
            {
                if (string.IsNullOrWhiteSpace(p)) return 0;
                var plain = Regex.Replace(p, @"<[^>]+>", string.Empty);
                return System.Net.WebUtility.HtmlDecode(plain).Trim().Length;
            });
            isOverFragmented = avgVisibleText < 1500;
        }

        return hasStyleTag || hasOversizedPage || isOverFragmented;
    }
}
