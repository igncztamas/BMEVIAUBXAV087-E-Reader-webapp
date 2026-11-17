namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;
using Microsoft.EntityFrameworkCore;

public class JsonDataStore : IDataStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private DatabaseSnapshot _snapshot = new();
    private bool _loaded;

    public JsonDataStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    private EbookDbContext CreateDbContext()
    {
        return new EbookDbContext($"Data Source={_filePath};Pooling=False");
    }

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

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        EnsureDirectoryExists();

        await using var db = CreateDbContext();
        await EnsureSchemaAsync(db);

        _snapshot = new DatabaseSnapshot
        {
            Users = await db.Users.AsNoTracking().ToListAsync(),
            Sessions = await db.Sessions.AsNoTracking().ToListAsync(),
            Books = await db.Books.Include(b => b.Bookmarks).AsNoTracking().ToListAsync()
        };

        await DataStoreBackfillHelper.BackfillExistingBooksAsync(_snapshot.Books, db);

        _loaded = true;
    }

    private void EnsureDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static async Task EnsureSchemaAsync(EbookDbContext db)
    {
        await db.Database.EnsureCreatedAsync();

        string[] alterQueries =
        [
            "ALTER TABLE Books ADD COLUMN Author TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Books ADD COLUMN ZoomLevel INTEGER NOT NULL DEFAULT 100;",
            "ALTER TABLE Books ADD COLUMN TableOfContents TEXT NOT NULL DEFAULT '[]';"
        ];

        foreach (var sql in alterQueries)
        {
            try { await db.Database.ExecuteSqlRawAsync(sql); } catch { /* Column already exists */ }
        }
    }

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

    private async Task SyncUsersAndSessionsAsync(EbookDbContext db)
    {
        await db.Users.ExecuteDeleteAsync();
        await db.Sessions.ExecuteDeleteAsync();
        await db.Users.AddRangeAsync(_snapshot.Users);
        await db.Sessions.AddRangeAsync(_snapshot.Sessions);
    }

    private async Task SyncBooksAsync(EbookDbContext db)
    {
        var existingBookIds = (await db.Books.Select(b => b.Id).ToListAsync()).ToHashSet();
        var snapshotBookIds = _snapshot.Books.Select(b => b.Id).ToHashSet();

        await db.Books.Where(b => !snapshotBookIds.Contains(b.Id)).ExecuteDeleteAsync();

        foreach (var book in _snapshot.Books)
        {
            if (!existingBookIds.Contains(book.Id))
            {
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
                await db.Books
                    .Where(b => b.Id == book.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.CurrentPage, book.CurrentPage)
                        .SetProperty(b => b.LastPositionUpdatedAtUtc, book.LastPositionUpdatedAtUtc)
                        .SetProperty(b => b.ZoomLevel, book.ZoomLevel <= 0 ? 100 : book.ZoomLevel));
            }
        }
    }

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
