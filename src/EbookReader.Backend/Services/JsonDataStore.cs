using System.Text.Json;
using EbookReader.Backend.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EbookReader.Backend.Services;

public class EbookDbContext : DbContext
{
    private readonly string? _connectionString;

    public EbookDbContext()
    {
    }

    public EbookDbContext(string? connectionString)
    {
        _connectionString = connectionString;
    }

    public EbookDbContext(DbContextOptions<EbookDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<SessionToken> Sessions => Set<SessionToken>();
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured && !string.IsNullOrWhiteSpace(_connectionString))
        {
            optionsBuilder.UseSqlite(_connectionString);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>().HasKey(u => u.Id);
        modelBuilder.Entity<SessionToken>().HasKey(s => s.Token);
        modelBuilder.Entity<Bookmark>().HasKey(b => b.Id);
        modelBuilder.Entity<Book>(eb =>
        {
            eb.HasKey(b => b.Id);
            eb.Property(b => b.Pages).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
            eb.HasMany(b => b.Bookmarks).WithOne().HasForeignKey(bm => bm.BookId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public class JsonDataStore : IDataStore
{
    private readonly string? _filePath;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private DatabaseSnapshot _snapshot = new();
    private bool _loaded;

    public JsonDataStore(string? filePath = null)
    {
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
        if (_loaded)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_filePath))
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_filePath))
            {
                try
                {
                    var header = new byte[1];
                    using var fs = File.OpenRead(_filePath);
                    if (fs.Read(header, 0, 1) > 0 && header[0] == '{')
                    {
                        fs.Dispose();
                        var json = await File.ReadAllTextAsync(_filePath);
                        var loaded = JsonSerializer.Deserialize<DatabaseSnapshot>(json);
                        if (loaded is not null)
                        {
                            _snapshot = loaded;
                            _loaded = true;
                            File.Delete(_filePath);
                            await PersistAsync();
                            return;
                        }
                    }
                }
                catch
                {
                    // If JSON check fails, proceed with SQLite loading
                }
            }

            await using var db = CreateDbContext();
            await db.Database.EnsureCreatedAsync();

            var users = await db.Users.AsNoTracking().ToListAsync();
            var sessions = await db.Sessions.AsNoTracking().ToListAsync();
            var books = await db.Books.Include(b => b.Bookmarks).AsNoTracking().ToListAsync();

            _snapshot = new DatabaseSnapshot
            {
                Users = users,
                Sessions = sessions,
                Books = books
            };
        }

        _loaded = true;
    }

    private async Task PersistAsync()
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        await using var tx = await db.Database.BeginTransactionAsync();

        // 1. Users and Sessions
        await db.Users.ExecuteDeleteAsync();
        await db.Sessions.ExecuteDeleteAsync();
        await db.Users.AddRangeAsync(_snapshot.Users);
        await db.Sessions.AddRangeAsync(_snapshot.Sessions);

        // 2. Books
        var existingBookIds = (await db.Books.Select(b => b.Id).ToListAsync()).ToHashSet();
        var snapshotBookIds = _snapshot.Books.Select(b => b.Id).ToHashSet();

        await db.Books.Where(b => !snapshotBookIds.Contains(b.Id)).ExecuteDeleteAsync();

        foreach (var book in _snapshot.Books)
        {
            if (!existingBookIds.Contains(book.Id))
            {
                var newBook = new Book
                {
                    Id = book.Id,
                    UserId = book.UserId,
                    Title = book.Title,
                    FileName = book.FileName,
                    Format = book.Format,
                    ContentHash = book.ContentHash,
                    FileSizeBytes = book.FileSizeBytes,
                    FileBytes = book.FileBytes,
                    Pages = new List<string>(book.Pages),
                    CurrentPage = book.CurrentPage,
                    LastPositionUpdatedAtUtc = book.LastPositionUpdatedAtUtc,
                    UploadedAtUtc = book.UploadedAtUtc,
                    Bookmarks = new List<Bookmark>()
                };
                db.Books.Add(newBook);
            }
            else
            {
                await db.Books
                    .Where(b => b.Id == book.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.CurrentPage, book.CurrentPage)
                        .SetProperty(b => b.LastPositionUpdatedAtUtc, book.LastPositionUpdatedAtUtc));
            }
        }

        // 3. Bookmarks
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

        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
