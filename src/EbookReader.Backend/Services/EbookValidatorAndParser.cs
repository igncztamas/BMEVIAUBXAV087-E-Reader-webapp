namespace EbookReader.Backend.Services;

using System.Security.Cryptography;
using EbookReader.Backend.Models;

public class EbookValidatorAndParser : IEbookValidatorAndParser
{
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    public long MaxFileSizeBytes { get; }

    public EbookValidatorAndParser(long maxFileSizeBytes = DefaultMaxFileSizeBytes)
    {
        MaxFileSizeBytes = maxFileSizeBytes;
    }

    public ServiceResult<ParsedEbook> ValidateAndParse(string fileName, byte[]? fileBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.InvalidFilename, "Filename is required.");
        }

        if (fileBytes is null || fileBytes.Length == 0)
        {
            return ServiceResult<ParsedEbook>.Failure(400, EbookErrorCodes.EmptyFile, "The uploaded file is empty.");
        }

        if (fileBytes.Length > MaxFileSizeBytes)
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.FileTooLarge,
                $"File size ({fileBytes.Length} bytes) exceeds the allowed limit ({MaxFileSizeBytes} bytes).");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension != ".pdf" && extension != ".epub")
        {
            return ServiceResult<ParsedEbook>.Failure(
                400,
                EbookErrorCodes.UnsupportedFormat,
                "Unsupported file format. Only PDF (.pdf) and EPUB (.epub) files are supported.");
        }

        var contentHash = Convert.ToHexString(SHA256.HashData(fileBytes));

        return extension == ".pdf"
            ? PdfEbookParser.ParseAndValidate(fileBytes, contentHash)
            : EpubEbookParser.ParseAndValidate(fileBytes, contentHash);
    }
}
