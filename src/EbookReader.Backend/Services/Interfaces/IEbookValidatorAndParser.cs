using EbookReader.Backend.Models;

namespace EbookReader.Backend.Services;

public interface IEbookValidatorAndParser
{
    long MaxFileSizeBytes { get; }
    ServiceResult<ParsedEbook> ValidateAndParse(string fileName, byte[]? fileBytes);
}
