namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Thread-safe persistence store using an in-memory snapshot backed by SQLite database storage.
/// </summary>
public class JsonDataStore : IDataStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private DatabaseSnapshot _snapshot = new();
    private bool _loaded;

    /// <summary>
    /// Initializes a new instance of <see cref="JsonDataStore"/> with the SQLite database file path.
    /// </summary>
    /// <param name="filePath">Absolute or relative file path to the SQLite database.</param>
    /// <exception cref="ArgumentException">Thrown when filePath is null or whitespace.</exception>
    public JsonDataStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <summary>
    /// Creates a new <see cref="EbookDbContext"/> instance targeting the configured SQLite file.
    /// </summary>
    private EbookDbContext CreateDbContext()
    {
        return new EbookDbContext($"Data Source={_filePath};Pooling=False");
    }

    /// <inheritdoc/>
    public async Task<TResult> ReadAsync<TResult>(Func<DatabaseSnapshot, TResult> query)
    {
        await _mutex.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            return query(_snapshot);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<TResult> WriteAsync<TResult>(Func<DatabaseSnapshot, TResult> mutator)
    {
        await _mutex.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            var result = mutator(_snapshot);
            await PersistAsync();
            return result;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// Ensures database directory, schema migrations, and initial snapshot data are loaded from SQLite.
    /// </summary>
    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        EnsureDirectoryExists();

        await using var db = CreateDbContext();
        await EnsureSchemaAsync(db);

        // Load tables into in-memory snapshot
        _snapshot = new DatabaseSnapshot
        {
            Users = await db.Users.AsNoTracking().ToListAsync(),
            Sessions = await db.Sessions.AsNoTracking().ToListAsync(),
            Books = await db.Books.Include(b => b.Bookmarks).AsNoTracking().ToListAsync()
        };

        // Run background backfill migration for books missing TOC or repagination
        await DataStoreBackfillHelper.BackfillExistingBooksAsync(_snapshot.Books, db);

        _loaded = true;
    }

    /// <summary>
    /// Ensures the directory containing the SQLite database file exists.
    /// </summary>
    private void EnsureDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Ensures the SQLite database schema is created and applies necessary column alterations.
    /// </summary>
    private static async Task EnsureSchemaAsync(EbookDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        string[] alterQueries =
        [
            "ALTER TABLE Books ADD COLUMN Author TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Books ADD COLUMN ZoomLevel INTEGER NOT NULL DEFAULT 100;",
            "ALTER TABLE Books ADD COLUMN TableOfContents TEXT NOT NULL DEFAULT '[]';"
        ];

        // Safely execute idempotent ALTER TABLE statements
        foreach (var sql in alterQueries)
        {
            try { await db.Database.ExecuteSqlRawAsync(sql); } catch { /* Column already exists */ }
        }
    }

    /// <summary>
    /// Persists current in-memory snapshot state into SQLite inside an atomic transaction.
    /// </summary>
    private async Task PersistAsync()
    {
        EnsureDirectoryExists();

        await using var db = CreateDbContext();
        await EnsureSchemaAsync(db);

        await using var tx = await db.Database.BeginTransactionAsync();

        await SyncUsersAndSessionsAsync(db);
        await SyncBooksAsync(db);
        await SyncBookmarksAsync(db);

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    /// <summary>
    /// Synchronizes users and session tokens from snapshot to the database.
    /// </summary>
    private async Task SyncUsersAndSessionsAsync(EbookDbContext db)
    {
        await db.Users.ExecuteDeleteAsync();
        await db.Sessions.ExecuteDeleteAsync();
        await db.Users.AddRangeAsync(_snapshot.Users);
        await db.Sessions.AddRangeAsync(_snapshot.Sessions);
    }

    /// <summary>
    /// Synchronizes books collection, updating reading progress and inserting new books.
    /// </summary>
    private async Task SyncBooksAsync(EbookDbContext db)
    {
        var existingBookIds = (await db.Books.Select(b => b.Id).ToListAsync()).ToHashSet();
        var snapshotBookIds = _snapshot.Books.Select(b => b.Id).ToHashSet();

        // Remove books deleted from snapshot
        await db.Books.Where(b => !snapshotBookIds.Contains(b.Id)).ExecuteDeleteAsync();

        foreach (var book in _snapshot.Books)
        {
            if (!existingBookIds.Contains(book.Id))
            {
                // Insert new book entity
                db.Books.Add(new Book
                {
                    Id = book.Id,
                    UserId = book.UserId,
                    Title = book.Title,
                    Author = book.Author,
                    FileName = book.FileName,
                    Format = book.Format,
                    ContentHash = book.ContentHash,
                    FileSizeBytes = book.FileSizeBytes,
                    FileBytes = book.FileBytes,
                    Pages = new List<string>(book.Pages),
                    CurrentPage = book.CurrentPage,
                    ZoomLevel = book.ZoomLevel <= 0 ? 100 : book.ZoomLevel,
                    TableOfContents = book.TableOfContents ?? new List<TocItem>(),
                    LastPositionUpdatedAtUtc = book.LastPositionUpdatedAtUtc,
                    UploadedAtUtc = book.UploadedAtUtc,
                    Bookmarks = new List<Bookmark>()
                });
            }
            else
            {
                // Update reading position and zoom level on existing book
                await db.Books
                    .Where(b => b.Id == book.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.CurrentPage, book.CurrentPage)
                        .SetProperty(b => b.LastPositionUpdatedAtUtc, book.LastPositionUpdatedAtUtc)
                        .SetProperty(b => b.ZoomLevel, book.ZoomLevel <= 0 ? 100 : book.ZoomLevel));
            }
        }
    }

    /// <summary>
    /// Synchronizes all bookmarks across all books.
    /// </summary>
    private async Task SyncBookmarksAsync(EbookDbContext db)
    {
        await db.Bookmarks.ExecuteDeleteAsync();
        var allBookmarks = _snapshot.Books
            .SelectMany(b => b.Bookmarks)
            .Select(bm => new Bookmark
            {
                Id = bm.Id,
                BookId = bm.BookId,
                Name = bm.Name,
                PageNumber = bm.PageNumber,
                CreatedAtUtc = bm.CreatedAtUtc
            })
            .ToList();
        await db.Bookmarks.AddRangeAsync(allBookmarks);
    }
}
