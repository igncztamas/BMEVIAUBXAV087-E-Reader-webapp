namespace EbookReader.Backend.Services;

using Microsoft.Extensions.DependencyInjection;

public static class EbookBackendServiceExtensions
{
    public static IServiceCollection AddEbookBackendServices(this IServiceCollection services, string dataFilePath)
    {
        services.AddSingleton<IDataStore>(_ => new JsonDataStore(dataFilePath));
        services.AddSingleton<IEbookValidatorAndParser, EbookValidatorAndParser>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IBookService, BookService>();
        return services;
    }
}
