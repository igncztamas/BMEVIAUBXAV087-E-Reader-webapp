using EbookReader.Backend.Models;

namespace EbookReader.Backend.Services;

public record ParsedEbook(
    string Format,
    string ContentHash,
    List<string> Pages,
    string? MetadataTitle = null,
    string? MetadataAuthor = null,
    List<TocItem>? TableOfContents = null);
