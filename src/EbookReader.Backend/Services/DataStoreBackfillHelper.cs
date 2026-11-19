namespace EbookReader.Backend.Services;

using System.Text.RegularExpressions;
using EbookReader.Backend.Models;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Helper class providing background migration and backfill logic for existing books in the database.
/// </summary>
public static class DataStoreBackfillHelper
{
    /// <summary>
    /// Backfills missing table of contents and performs repagination for previously stored books.
    /// </summary>
    /// <param name="books">The list of books to inspect and update.</param>
    /// <param name="db">The active database context for applying updates.</param>
    public static async Task BackfillExistingBooksAsync(List<Book> books, EbookDbContext db)
    {
        foreach (var book in books)
        {
            // Skip books without stored file bytes
            if (book.FileBytes == null || book.FileBytes.Length == 0) continue;

            bool needsRepagination = NeedsRepagination(book);
            bool missingToc = book.TableOfContents == null || book.TableOfContents.Count == 0;

            // Skip if book already has valid pagination and table of contents
            if (!needsRepagination && !missingToc) continue;

            // Re-parse the book file to extract updated pagination and TOC
            var parser = new EbookValidatorAndParser();
            var parseResult = parser.ValidateAndParse(book.FileName, book.FileBytes);
            if (!parseResult.IsSuccess || parseResult.Data == null) continue;

            bool updated = false;

            // Backfill TOC if missing
            if (missingToc && parseResult.Data.TableOfContents is { Count: > 0 })
            {
                book.TableOfContents = parseResult.Data.TableOfContents;
                updated = true;
            }

            // Repaginate pages and clamp current position if needed
            if (needsRepagination && parseResult.Data.Pages is { Count: > 0 })
            {
                book.Pages = parseResult.Data.Pages;
                book.CurrentPage = Math.Clamp(book.CurrentPage, 1, book.Pages.Count);
                updated = true;
            }

            // Persist changes to SQLite
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

    /// <summary>
    /// Determines whether an EPUB book requires repagination due to style tags, oversized pages, or fragmentation.
    /// </summary>
    /// <param name="book">The book entity to check.</param>
    /// <returns>True if the book should be repaginated; otherwise false.</returns>
    private static bool NeedsRepagination(Book book)
    {
        // Only EPUB books with existing pages are candidates for repagination
        if (!string.Equals(book.Format, "EPUB", StringComparison.OrdinalIgnoreCase) ||
            book.Pages == null || book.Pages.Count == 0)
        {
            return false;
        }

        // Check for style tag contamination or oversized text blocks
        bool hasStyleTag = book.Pages.Any(p => p != null && p.Contains("<style", StringComparison.OrdinalIgnoreCase));
        bool hasOversizedPage = book.Pages.Any(p => p != null && p.Length > 5000);
        bool isOverFragmented = false;

        // Check if pages are over-fragmented with too little visible text per page
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
