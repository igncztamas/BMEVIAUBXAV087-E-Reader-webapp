namespace EbookReader.Backend.Services;

using System.Security.Cryptography;
using EbookReader.Backend.Models;

/// <summary>
/// Validates uploaded e-book files against size and format constraints, delegating content extraction to format-specific parsers.
/// </summary>
public class EbookValidatorAndParser : IEbookValidatorAndParser
{
    /// <summary>Default maximum file size limit (10 megabytes).</summary>
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;

    /// <inheritdoc/>
    public long MaxFileSizeBytes { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="EbookValidatorAndParser"/> with an optional file size limit.
    /// </summary>
    /// <param name="maxFileSizeBytes">Maximum allowed file size in bytes (defaults to 10 MB).</param>
    public EbookValidatorAndParser(long maxFileSizeBytes = DefaultMaxFileSizeBytes)
    {
        MaxFileSizeBytes = maxFileSizeBytes;
    }

    /// <inheritdoc/>
    public ServiceResult<ParsedEbook> ValidateAndParse(string fileName, byte[]? fileBytes)
    {
        // Validation guard: non-empty filename required
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.InvalidFilename, "Filename is required.");
        }

        // Validation guard: non-empty file content required
        if (fileBytes is null || fileBytes.Length == 0)
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.EmptyFile, "The uploaded file is empty.");
        }

        // Validation guard: file size limit check
        if (fileBytes.Length > MaxFileSizeBytes)
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.FileTooLarge,
                $"File size ({fileBytes.Length} bytes) exceeds the allowed limit ({MaxFileSizeBytes} bytes).");
        }

        // Validation guard: format extension check (.pdf and .epub only)
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension != ".pdf" && extension != ".epub")
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.UnsupportedFormat,
                "Unsupported file format. Only PDF (.pdf) and EPUB (.epub) files are supported.");
        }

        // Compute SHA-256 content hash for duplicate detection
        var contentHash = Convert.ToHexString(SHA256.HashData(fileBytes));

        // Delegate parsing to specialized format parser
        return extension == ".pdf"
            ? PdfEbookParser.ParseAndValidate(fileBytes, contentHash)
            : EpubEbookParser.ParseAndValidate(fileBytes, contentHash);
    }
}
