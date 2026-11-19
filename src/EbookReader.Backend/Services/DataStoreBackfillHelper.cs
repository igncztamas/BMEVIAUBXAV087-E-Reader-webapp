namespace EbookReader.Backend.Services;

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
            BackfillBookIfNeeded(book, db);
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Backfills table of contents and repaginates pages for a single book when required.
    /// </summary>
    /// <param name="book">The book entity to check and backfill.</param>
    /// <param name="db">The database context used to persist backfilled data.</param>
    public static void BackfillBookIfNeeded(Book book, EbookDbContext db)
    {
        // Skip books without stored file bytes
        if (book.FileBytes == null || book.FileBytes.Length == 0) return;

        bool needsRepagination = NeedsRepagination(book);

        // Only EPUBs with null TOC are candidates for TOC backfill; PDFs without outlines legitimately have []
        bool isEpub = string.Equals(book.Format, "EPUB", StringComparison.OrdinalIgnoreCase);
        bool missingToc = isEpub && book.TableOfContents == null;

        // Skip if book already has clean pagination and table of contents
        if (!needsRepagination && !missingToc) return;

        // Re-parse the book file to extract updated pagination and TOC
        var parser = new EbookValidatorAndParser();
        var parseResult = parser.ValidateAndParse(book.FileName, book.FileBytes);
        if (!parseResult.IsSuccess || parseResult.Data == null) return;

        bool updated = false;

        // Backfill TOC if missing
        if (missingToc)
        {
            book.TableOfContents = parseResult.Data.TableOfContents ?? new List<TocItem>();
            updated = true;
        }

        // Repaginate pages and clamp current position if needed
        if (needsRepagination && parseResult.Data.Pages is { Count: > 0 })
        {
            book.Pages = parseResult.Data.Pages;
            book.TotalPages = book.Pages.Count;
            book.CurrentPage = Math.Clamp(book.CurrentPage, 1, book.Pages.Count);
            updated = true;
        }

        // Persist changes to SQLite
        if (updated)
        {
            db.Books
                .Where(b => b.Id == book.Id)
                .ExecuteUpdate(s => s
                    .SetProperty(b => b.TableOfContents, book.TableOfContents)
                    .SetProperty(b => b.Pages, book.Pages)
                    .SetProperty(b => b.TotalPages, book.TotalPages)
                    .SetProperty(b => b.CurrentPage, book.CurrentPage));
        }
    }

    /// <summary>
    /// Determines whether an EPUB book requires repagination due to raw style tag contamination.
    /// </summary>
    /// <param name="book">The book entity to check.</param>
    /// <returns>True if the book contains style tags in its page HTML; otherwise false.</returns>
    private static bool NeedsRepagination(Book book)
    {
        // Only EPUB books with existing pages are candidates for repagination
        if (!string.Equals(book.Format, "EPUB", StringComparison.OrdinalIgnoreCase) ||
            book.Pages == null || book.Pages.Count == 0)
        {
            return false;
        }

        // Only trigger repagination if raw style tags leaked into page HTML from legacy parser
        return book.Pages.Any(p => p != null && p.Contains("<style", StringComparison.OrdinalIgnoreCase));
    }
}
