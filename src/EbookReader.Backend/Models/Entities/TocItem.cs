namespace EbookReader.Backend.Models;

public class TocItem
{
    public string Title { get; set; } = string.Empty;
    public int PageNumber { get; set; } = 1;
    public int Level { get; set; } = 0;
}
