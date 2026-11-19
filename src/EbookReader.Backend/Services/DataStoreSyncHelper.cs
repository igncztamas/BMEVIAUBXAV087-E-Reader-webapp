namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Helper class managing differential synchronization between in-memory database snapshots and SQLite tables.
/// </summary>
public class DataStoreSyncHelper
{
    private readonly HashSet<Guid> _persistedUserIds = new();
    private readonly HashSet<string> _persistedSessionTokens = new();
    private readonly Dictionary<Guid, (int CurrentPage, int ZoomLevel, DateTimeOffset LastUpdated)> _persistedBookState = new();
    private readonly HashSet<Guid> _persistedBookmarkIds = new();

    /// <summary>
    /// Caches initial snapshot entity states to allow differential database updates on subsequent writes.
    /// </summary>
    /// <param name="snapshot">The newly loaded database snapshot.</param>
    public void InitializePersistedState(DatabaseSnapshot snapshot)
    {
        _persistedUserIds.Clear();
        _persistedUserIds.UnionWith(snapshot.Users.Select(u => u.Id));

        _persistedSessionTokens.Clear();
        _persistedSessionTokens.UnionWith(snapshot.Sessions.Select(s => s.Token));

        _persistedBookState.Clear();
        foreach (var book in snapshot.Books)
        {
            _persistedBookState[book.Id] = (book.CurrentPage, book.ZoomLevel, book.LastPositionUpdatedAtUtc);
        }

        _persistedBookmarkIds.Clear();
        _persistedBookmarkIds.UnionWith(snapshot.Books.SelectMany(b => b.Bookmarks).Select(bm => bm.Id));
    }

    /// <summary>
    /// Synchronizes users and session tokens, avoiding full table rewrites when only a session was added or removed.
    /// </summary>
    public async Task SyncUsersAndSessionsAsync(DatabaseSnapshot snapshot, EbookDbContext db)
    {
        var currentUsers = snapshot.Users;

        // Re-sync users table only when user count or IDs change
        if (currentUsers.Count != _persistedUserIds.Count || !currentUsers.All(u => _persistedUserIds.Contains(u.Id)))
        {
            await db.Users.ExecuteDeleteAsync();
            await db.Users.AddRangeAsync(currentUsers);
            _persistedUserIds.Clear();
            _persistedUserIds.UnionWith(currentUsers.Select(u => u.Id));
        }

        // Incrementally sync sessions
        var currentTokens = snapshot.Sessions.Select(s => s.Token).ToHashSet();
        var removedTokens = _persistedSessionTokens.Except(currentTokens).ToList();
        if (removedTokens.Count > 0)
        {
            await db.Sessions.Where(s => removedTokens.Contains(s.Token)).ExecuteDeleteAsync();
        }

        var addedSessions = snapshot.Sessions.Where(s => !_persistedSessionTokens.Contains(s.Token)).ToList();
        if (addedSessions.Count > 0)
        {
            await db.Sessions.AddRangeAsync(addedSessions);
        }

        _persistedSessionTokens.Clear();
        _persistedSessionTokens.UnionWith(currentTokens);
    }

    /// <summary>
    /// Synchronizes books collection, executing updates only for books whose progress, zoom, or timestamp changed.
    /// </summary>
    public async Task SyncBooksAsync(DatabaseSnapshot snapshot, EbookDbContext db)
    {
        var currentBooks = snapshot.Books;
        var currentBookIds = currentBooks.Select(b => b.Id).ToHashSet();

        // Remove deleted books
        var removedBookIds = _persistedBookState.Keys.Where(id => !currentBookIds.Contains(id)).ToList();
        if (removedBookIds.Count > 0)
        {
            await db.Books.Where(b => removedBookIds.Contains(b.Id)).ExecuteDeleteAsync();
            foreach (var id in removedBookIds) _persistedBookState.Remove(id);
        }

        // Add new books or update modified reading progress/zoom
        foreach (var book in currentBooks)
        {
            if (!_persistedBookState.TryGetValue(book.Id, out var state))
            {
                db.Books.Add(CreatePersistedBook(book));
                _persistedBookState[book.Id] = (book.CurrentPage, book.ZoomLevel, book.LastPositionUpdatedAtUtc);
            }
            else if (state.CurrentPage != book.CurrentPage ||
                     state.ZoomLevel != book.ZoomLevel ||
                     state.LastUpdated != book.LastPositionUpdatedAtUtc)
            {
                // Update only when progress, timestamp, or zoom level changed
                await db.Books
                    .Where(b => b.Id == book.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.CurrentPage, book.CurrentPage)
                        .SetProperty(b => b.LastPositionUpdatedAtUtc, book.LastPositionUpdatedAtUtc)
                        .SetProperty(b => b.ZoomLevel, book.ZoomLevel <= 0 ? 100 : book.ZoomLevel));

                _persistedBookState[book.Id] = (book.CurrentPage, book.ZoomLevel, book.LastPositionUpdatedAtUtc);
            }
        }
    }

    /// <summary>
    /// Synchronizes bookmarks incrementally, skipping database writes entirely if bookmarks are untouched.
    /// </summary>
    public async Task SyncBookmarksAsync(DatabaseSnapshot snapshot, EbookDbContext db)
    {
        var currentBookmarks = snapshot.Books.SelectMany(b => b.Bookmarks).ToList();
        var currentIds = currentBookmarks.Select(bm => bm.Id).ToHashSet();

        // Skip completely if no bookmarks were added or removed
        if (_persistedBookmarkIds.SetEquals(currentIds))
        {
            return;
        }

        var removedIds = _persistedBookmarkIds.Except(currentIds).ToList();
        if (removedIds.Count > 0)
        {
            await db.Bookmarks.Where(b => removedIds.Contains(b.Id)).ExecuteDeleteAsync();
        }

        var addedBookmarks = currentBookmarks
            .Where(bm => !_persistedBookmarkIds.Contains(bm.Id))
            .Select(bm => new Bookmark
            {
                Id = bm.Id,
                BookId = bm.BookId,
                Name = bm.Name,
                PageNumber = bm.PageNumber,
                CreatedAtUtc = bm.CreatedAtUtc
            })
            .ToList();

        if (addedBookmarks.Count > 0)
        {
            await db.Bookmarks.AddRangeAsync(addedBookmarks);
        }

        _persistedBookmarkIds.Clear();
        _persistedBookmarkIds.UnionWith(currentIds);
    }

    /// <summary>
    /// Clones a <see cref="Book"/> entity for detached persistence insertion.
    /// </summary>
    private static Book CreatePersistedBook(Book b) => new()
    {
        Id = b.Id,
        UserId = b.UserId,
        Title = b.Title,
        Author = b.Author,
        FileName = b.FileName,
        Format = b.Format,
        ContentHash = b.ContentHash,
        FileSizeBytes = b.FileSizeBytes,
        FileBytes = b.FileBytes,
        Pages = new List<string>(b.Pages),
        TotalPages = b.TotalPages > 0 ? b.TotalPages : Math.Max(1, b.Pages.Count),
        CurrentPage = b.CurrentPage,
        ZoomLevel = b.ZoomLevel <= 0 ? 100 : b.ZoomLevel,
        TableOfContents = b.TableOfContents ?? new List<TocItem>(),
        LastPositionUpdatedAtUtc = b.LastPositionUpdatedAtUtc,
        UploadedAtUtc = b.UploadedAtUtc,
        Bookmarks = new List<Bookmark>()
    };
}
