using EbookReader.Backend.Services;

namespace EbookReader.Tests.Helpers;

/// <summary>
/// Creates wired backend service instances backed by an in-memory or file-backed store.
/// </summary>
internal static class TestServiceFactory
{
    public static (IAuthService Auth, IBookService Books, IEbookValidatorAndParser Validator) CreateServices(
        string? filePath = null,
        long maxFileSizeBytes = 10 * 1024 * 1024)
    {
        IDataStore store = string.IsNullOrWhiteSpace(filePath)
            ? new InMemoryDataStore()
            : new JsonDataStore(filePath);
        IEbookValidatorAndParser validator = new EbookValidatorAndParser(maxFileSizeBytes);
        IAuthService auth = new AuthService(store);
        IBookService books = new BookService(store, validator);
        return (auth, books, validator);
    }
}
