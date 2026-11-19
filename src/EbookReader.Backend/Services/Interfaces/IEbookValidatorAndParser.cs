using EbookReader.Backend.Models;

namespace EbookReader.Backend.Services;

/// <summary>
/// Service interface responsible for validating uploaded e-book file size, extensions, and parsing PDF/EPUB contents.
/// </summary>
public interface IEbookValidatorAndParser
{
    /// <summary>
    /// Maximum allowed upload file size in bytes.
    /// </summary>
    long MaxFileSizeBytes { get; }

    /// <summary>
    /// Validates file integrity and extracts pages, metadata, and table of contents.
    /// </summary>
    /// <param name="fileName">The uploaded file's name.</param>
    /// <param name="fileBytes">The raw file bytes.</param>
    /// <returns>A <see cref="ServiceResult{T}"/> containing <see cref="ParsedEbook"/> on success, or validation failure.</returns>
    ServiceResult<ParsedEbook> ValidateAndParse(string fileName, byte[]? fileBytes);
}
