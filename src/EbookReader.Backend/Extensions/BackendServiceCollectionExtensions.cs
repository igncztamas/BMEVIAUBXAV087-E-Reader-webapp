namespace EbookReader.Backend.Services;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering backend services into the dependency injection container.
/// </summary>
public static class EbookBackendServiceExtensions
{
    /// <summary>
    /// Registers core backend singletons including data storage, validation, authentication, and book management.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="dataFilePath">The SQLite database file path used by <see cref="JsonDataStore"/>.</param>
    /// <returns>The modified service collection for method chaining.</returns>
    public static IServiceCollection AddEbookBackendServices(this IServiceCollection services, string dataFilePath)
    {
        // Thread-safe data store backed by SQLite
        services.AddSingleton<IDataStore>(_ => new JsonDataStore(dataFilePath));

        // PDF/EPUB integrity validator and parser
        services.AddSingleton<IEbookValidatorAndParser, EbookValidatorAndParser>();

        // PBKDF2 authentication and token session manager
        services.AddSingleton<IAuthService, AuthService>();

        // Book library management, progress updates, and bookmarks
        services.AddSingleton<IBookService, BookService>();

        return services;
    }
}
