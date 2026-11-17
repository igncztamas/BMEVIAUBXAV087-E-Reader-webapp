namespace EbookReader.Backend.Models;

public class Bookmark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PageNumber { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
