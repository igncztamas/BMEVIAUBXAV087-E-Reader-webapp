using EbookReader.Backend.Models;
using EbookReader.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

var dataFilePath = builder.Configuration["DataStore:FilePath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "ebook_store.json");

builder.Services.AddEbookBackendServices(dataFilePath);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();

EbookApiEndpoints.MapEndpoints(app);

app.Run();

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

public static class EbookApiEndpoints
{
    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "EbookReader.Backend" }));

        app.MapPost("/api/auth/register", async (RegisterRequest request, IAuthService authService) =>
        {
            var result = await authService.RegisterAsync(request.Username, request.Password);
            return ToHttpResult(result);
        });

        app.MapPost("/api/auth/login", async (LoginRequest request, IAuthService authService) =>
        {
            var result = await authService.LoginAsync(request.Username, request.Password);
            return ToHttpResult(result);
        });

        app.MapPost("/api/auth/logout", async (HttpContext http, IAuthService authService) =>
        {
            var token = GetAuthHeader(http);
            await authService.LogoutAsync(token);
            return Results.Ok(new { message = "Logged out successfully." });
        });

        app.MapGet("/api/auth/me", async (HttpContext http, IAuthService authService) =>
        {
            var user = await AuthenticateAsync(http, authService);
            if (user is null)
            {
                return UnauthorizedResult();
            }

            return Results.Ok(user);
        });

        app.MapGet("/api/books", async (HttpContext http, IAuthService authService, IBookService bookService) =>
        {
            var user = await AuthenticateAsync(http, authService);
            if (user is null)
            {
                return UnauthorizedResult();
            }

            var books = await bookService.GetUserBooksAsync(user.Id);
            return Results.Ok(books);
        });

        app.MapPost("/api/books", async (HttpContext http, IAuthService authService, IBookService bookService) =>
        {
            var user = await AuthenticateAsync(http, authService);
            if (user is null)
            {
                return UnauthorizedResult();
            }

            if (http.Request.HasFormContentType)
            {
                var form = await http.Request.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                if (file is null)
                {
                    return Results.Json(
                        new ApiErrorResponse("No uploaded file found in request.", "MISSING_FILE"),
                        statusCode: 400);
                }

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                var customTitle = form["title"].FirstOrDefault();
                var uploadResult = await bookService.UploadBookAsync(user.Id, file.FileName, ms.ToArray(), customTitle);
                return ToHttpResult(uploadResult);
            }
            else
            {
                UploadBookRequest? request;
                try
                {
                    request = await http.Request.ReadFromJsonAsync<UploadBookRequest>();
                }
                catch
                {
                    return Results.Json(
                        new ApiErrorResponse("Invalid JSON request body.", "INVALID_JSON"),
                        statusCode: 400);
                }

                if (request is null)
                {
                    return Results.Json(
                        new ApiErrorResponse("Missing upload payload.", "MISSING_PAYLOAD"),
                        statusCode: 400);
                }

                byte[] fileBytes;
                try
                {
                    fileBytes = string.IsNullOrEmpty(request.FileBytesBase64)
                        ? Array.Empty<byte>()
                        : Convert.FromBase64String(request.FileBytesBase64);
                }
                catch (FormatException)
                {
                    return Results.Json(
                        new ApiErrorResponse("Invalid Base64 file content.", "INVALID_BASE64"),
                        statusCode: 400);
                }

                var uploadResult = await bookService.UploadBookAsync(user.Id, request.FileName, fileBytes, request.Title);
                return ToHttpResult(uploadResult);
            }
        });

        app.MapGet("/api/books/{id:guid}", async (Guid id, HttpContext http, IAuthService authService, IBookService bookService) =>
        {
            var user = await AuthenticateAsync(http, authService);
            if (user is null)
            {
                return UnauthorizedResult();
            }

            var result = await bookService.GetBookDetailAsync(user.Id, id);
            return ToHttpResult(result);
        });

        app.MapGet("/api/books/{id:guid}/pages/{pageNumber:int}", async (
            Guid id,
            int pageNumber,
            HttpContext http,
            IAuthService authService,
            IBookService bookService) =>
        {
            var user = await AuthenticateAsync(http, authService);
            if (user is null)
            {
                return UnauthorizedResult();
            }

            var result = await bookService.GetBookPageAsync(user.Id, id, pageNumber);
            return ToHttpResult(result);
        });

        app.MapPut("/api/books/{id:guid}/progress", async (
            Guid id,
            UpdateProgressRequest request,
            HttpContext http,
            IAuthService authService,
            IBookService bookService) =>
        {
            var user = await AuthenticateAsync(http, authService);
            if (user is null)
            {
                return UnauthorizedResult();
            }

            var result = await bookService.UpdateReadingProgressAsync(
                user.Id,
                id,
                request.PageNumber,
                request.ClientTimestampUtc);

            return ToHttpResult(result);
        });

        app.MapPost("/api/books/{id:guid}/bookmarks", async (
            Guid id,
            CreateBookmarkRequest request,
            HttpContext http,
            IAuthService authService,
            IBookService bookService) =>
        {
            var user = await AuthenticateAsync(http, authService);
            if (user is null)
            {
                return UnauthorizedResult();
            }

            var result = await bookService.AddBookmarkAsync(user.Id, id, request.Name, request.PageNumber);
            return ToHttpResult(result);
        });

        app.MapDelete("/api/books/{id:guid}/bookmarks/{bookmarkId:guid}", async (
            Guid id,
            Guid bookmarkId,
            HttpContext http,
            IAuthService authService,
            IBookService bookService) =>
        {
            var user = await AuthenticateAsync(http, authService);
            if (user is null)
            {
                return UnauthorizedResult();
            }

            var result = await bookService.DeleteBookmarkAsync(user.Id, id, bookmarkId);
            return ToHttpResult(result);
        });

        app.MapDelete("/api/books/{id:guid}", async (
            Guid id,
            HttpContext http,
            IAuthService authService,
            IBookService bookService) =>
        {
            var user = await AuthenticateAsync(http, authService);
            if (user is null)
            {
                return UnauthorizedResult();
            }

            var result = await bookService.DeleteBookAsync(user.Id, id);
            return ToHttpResult(result);
        });
    }

    private static string? GetAuthHeader(HttpContext http) =>
        http.Request.Headers.Authorization.FirstOrDefault();

    private static async Task<UserDto?> AuthenticateAsync(HttpContext http, IAuthService authService)
    {
        var token = GetAuthHeader(http);
        return await authService.ValidateTokenAsync(token);
    }

    private static IResult UnauthorizedResult() =>
        Results.Json(
            new ApiErrorResponse("Authentication required to perform this action.", EbookErrorCodes.Unauthorized),
            statusCode: 401);

    private static IResult ToHttpResult<T>(ServiceResult<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Json(result.Data, statusCode: result.StatusCode);
        }

        return Results.Json(
            new ApiErrorResponse(result.ErrorMessage ?? "Unknown error.", result.ErrorCode ?? "ERROR"),
            statusCode: result.StatusCode);
    }
}
