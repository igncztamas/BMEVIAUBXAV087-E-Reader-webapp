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
    private readonly DataStoreSyncHelper _syncHelper = new();
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
        await db.EnsureCustomSchemaAsync();

        // One-time migration: populate TotalPages for existing books created before this column existed
        if (await db.Books.AnyAsync(b => b.TotalPages <= 0))
        {
            var legacyBooks = await db.Books
                .Where(b => b.TotalPages <= 0)
                .Select(b => new { b.Id, b.Pages })
                .ToListAsync();

            foreach (var lb in legacyBooks)
            {
                int count = Math.Max(1, lb.Pages?.Count ?? 0);
                await db.Books
                    .Where(b => b.Id == lb.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(b => b.TotalPages, count));
            }
        }

        // Project lightweight book metadata without loading FileBytes or Pages JSON into memory
        var bookMetadata = await db.Books
            .AsNoTracking()
            .Select(b => new
            {
                b.Id,
                b.UserId,
                b.Title,
                b.Author,
                b.FileName,
                b.Format,
                b.ContentHash,
                b.FileSizeBytes,
                b.TotalPages,
                b.CurrentPage,
                b.ZoomLevel,
                b.LastPositionUpdatedAtUtc,
                b.UploadedAtUtc
            })
            .ToListAsync();

        var bookmarks = await db.Bookmarks.AsNoTracking().ToListAsync();
        var bookmarksByBook = bookmarks.GroupBy(bm => bm.BookId).ToDictionary(g => g.Key, g => g.ToList());

        var books = bookMetadata.Select(m =>
        {
            var book = new Book
            {
                Id = m.Id,
                UserId = m.UserId,
                Title = m.Title,
                Author = m.Author,
                FileName = m.FileName,
                Format = m.Format,
                ContentHash = m.ContentHash,
                FileSizeBytes = m.FileSizeBytes,
                TotalPages = m.TotalPages > 0 ? m.TotalPages : 1,
                CurrentPage = m.CurrentPage,
                ZoomLevel = m.ZoomLevel,
                LastPositionUpdatedAtUtc = m.LastPositionUpdatedAtUtc,
                UploadedAtUtc = m.UploadedAtUtc,
                Pages = new List<string>(),
                Bookmarks = bookmarksByBook.TryGetValue(m.Id, out var bList) ? bList : new List<Bookmark>(),
                TableOfContents = new List<TocItem>()
            };

            AttachContentLoader(book);
            return book;
        }).ToList();

        // Load users, sessions, and lightweight books into in-memory snapshot
        _snapshot = new DatabaseSnapshot
        {
            Users = await db.Users.AsNoTracking().ToListAsync(),
            Sessions = await db.Sessions.AsNoTracking().ToListAsync(),
            Books = books
        };

        // Initialize differential tracking cache
        _syncHelper.InitializePersistedState(_snapshot);

        _loaded = true;
    }

    /// <summary>
    /// Attaches an on-demand content loader callback that loads FileBytes, Pages, and TableOfContents when accessed.
    /// </summary>
    private void AttachContentLoader(Book targetBook)
    {
        targetBook.SetContentLoader(b =>
        {
            using var contentDb = CreateDbContext();
            var content = contentDb.Books
                .AsNoTracking()
                .Where(x => x.Id == b.Id)
                .Select(x => new
                {
                    x.FileBytes,
                    x.Pages,
                    x.TableOfContents,
                    x.TotalPages
                })
                .FirstOrDefault();

            if (content != null)
            {
                b.FileBytes = content.FileBytes ?? Array.Empty<byte>();
                b.Pages = content.Pages ?? new List<string>();
                b.TableOfContents = content.TableOfContents ?? new List<TocItem>();
                if (b.TotalPages <= 0)
                {
                    b.TotalPages = content.TotalPages > 0 ? content.TotalPages : Math.Max(1, b.Pages.Count);
                }

                DataStoreBackfillHelper.BackfillBookIfNeeded(b, contentDb);
            }
        });
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
    /// Persists current in-memory snapshot state into SQLite inside an atomic transaction using differential sync.
    /// </summary>
    private async Task PersistAsync()
    {
        EnsureDirectoryExists();

        await using var db = CreateDbContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        await _syncHelper.SyncUsersAndSessionsAsync(_snapshot, db);
        await _syncHelper.SyncBooksAsync(_snapshot, db);
        await _syncHelper.SyncBookmarksAsync(_snapshot, db);

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
