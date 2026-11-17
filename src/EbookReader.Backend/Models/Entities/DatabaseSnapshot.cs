namespace EbookReader.Backend.Models;

public class DatabaseSnapshot
{
    public List<User> Users { get; set; } = new();
    public List<Book> Books { get; set; } = new();
    public List<SessionToken> Sessions { get; set; } = new();
}
