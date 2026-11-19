namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;

/// <summary>
/// Output payload returned by e-book parsers containing extracted pages, metadata, content hash, and table of contents.
/// </summary>
/// <param name="Format">The parsed format ("PDF" or "EPUB").</param>
/// <param name="ContentHash">Hex-encoded SHA-256 hash of the input file bytes.</param>
/// <param name="Pages">List of extracted HTML or plain text page strings.</param>
/// <param name="MetadataTitle">Optional document title extracted from metadata.</param>
/// <param name="MetadataAuthor">Optional author extracted from metadata.</param>
/// <param name="TableOfContents">Optional table of contents items hierarchy.</param>
public record ParsedEbook(
    string Format,
    string ContentHash,
    List<string> Pages,
    string? MetadataTitle = null,
    string? MetadataAuthor = null,
    List<TocItem>? TableOfContents = null);
