namespace EbookReader.Backend.Services;

using System.Text.Json;
using EbookReader.Backend.Models;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Entity Framework Core database context for SQLite persistence of users, sessions, books, and bookmarks.
/// </summary>
public class EbookDbContext : DbContext
{
    private readonly string? _connectionString;

    /// <summary>
    /// Parameterless constructor for design-time tooling and mock setups.
    /// </summary>
    public EbookDbContext()
    {
    }

    /// <summary>
    /// Initializes a new instance with a SQLite connection string.
    /// </summary>
    /// <param name="connectionString">The SQLite database connection string.</param>
    public EbookDbContext(string? connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Initializes a new instance with typed DbContext options.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    public EbookDbContext(DbContextOptions<EbookDbContext> options) : base(options)
    {
    }

    /// <summary>Database set for registered users.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Database set for active authentication sessions.</summary>
    public DbSet<SessionToken> Sessions => Set<SessionToken>();

    /// <summary>Database set for uploaded books and metadata.</summary>
    public DbSet<Book> Books => Set<Book>();

    /// <summary>Database set for user bookmarks.</summary>
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    /// <summary>
    /// Ensures database creation and applies idempotent column additions for backward-compatible schemas.
    /// </summary>
    public async Task EnsureCustomSchemaAsync()
    {
        await Database.EnsureCreatedAsync();

        string[] alterQueries =
        [
            "ALTER TABLE Books ADD COLUMN Author TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE Books ADD COLUMN ZoomLevel INTEGER NOT NULL DEFAULT 100;",
            "ALTER TABLE Books ADD COLUMN TableOfContents TEXT NOT NULL DEFAULT '[]';",
            "ALTER TABLE Books ADD COLUMN TotalPages INTEGER NOT NULL DEFAULT 0;"
        ];

        // Safely execute idempotent ALTER TABLE statements
        foreach (var sql in alterQueries)
        {
            try { await Database.ExecuteSqlRawAsync(sql); } catch { /* Column already exists */ }
        }
    }

    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Configure SQLite provider if connection string is provided and not pre-configured
        if (!optionsBuilder.IsConfigured && !string.IsNullOrWhiteSpace(_connectionString))
        {
            optionsBuilder.UseSqlite(_connectionString);
        }
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure entity primary keys
        modelBuilder.Entity<User>().HasKey(u => u.Id);
        modelBuilder.Entity<SessionToken>().HasKey(s => s.Token);
        modelBuilder.Entity<Bookmark>().HasKey(b => b.Id);

        // Configure Book entity with JSON column conversions for Pages and TableOfContents
        modelBuilder.Entity<Book>(eb =>
        {
            eb.HasKey(b => b.Id);

            // Serialize pages list to JSON string in SQLite
            eb.Property(b => b.Pages).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

            // Serialize TOC items list to JSON string in SQLite
            eb.Property(b => b.TableOfContents).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<TocItem>>(v, (JsonSerializerOptions?)null) ?? new List<TocItem>());

            // Cascade delete bookmarks when book is deleted
            eb.HasMany(b => b.Bookmarks).WithOne().HasForeignKey(bm => bm.BookId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
