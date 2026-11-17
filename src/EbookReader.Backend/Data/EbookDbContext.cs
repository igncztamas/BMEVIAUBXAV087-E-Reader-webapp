namespace EbookReader.Backend.Services;

using System.Text.Json;
using EbookReader.Backend.Models;
using Microsoft.EntityFrameworkCore;

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
            eb.Property(b => b.TableOfContents).HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<TocItem>>(v, (JsonSerializerOptions?)null) ?? new List<TocItem>());
            eb.HasMany(b => b.Bookmarks).WithOne().HasForeignKey(bm => bm.BookId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
