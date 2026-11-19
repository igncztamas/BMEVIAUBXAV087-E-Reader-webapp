namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Configures and maps HTTP Minimal API endpoints for authentication, books, progress, and bookmarks.
/// </summary>
public static class EbookApiEndpoints
{
    /// <summary>
    /// Registers all route handlers on the provided endpoint route builder.
    /// </summary>
    /// <param name="app">The application endpoint route builder.</param>
    public static void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Health check endpoint
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "EbookReader.Backend" }));

        // User registration
        app.MapPost("/api/auth/register", async (RegisterRequest request, IAuthService authService) =>
        {
            var result = await authService.RegisterAsync(request.Username, request.Password);
            return ToHttpResult(result);
        });

        // User login
        app.MapPost("/api/auth/login", async (LoginRequest request, IAuthService authService) =>
        {
            var result = await authService.LoginAsync(request.Username, request.Password);
            return ToHttpResult(result);
        });

        // User logout
        app.MapPost("/api/auth/logout", async (HttpContext http, IAuthService authService) =>
        {
            var token = GetAuthHeader(http);
            await authService.LogoutAsync(token);
            return Results.Ok(new { message = "Logged out successfully." });
        });

        // Get currently authenticated user profile
        app.MapGet("/api/auth/me", async (HttpContext http, IAuthService authService) =>
            await WithUserAsync(http, authService, user => Task.FromResult(Results.Ok(user))));

        // List books for the authenticated user
        app.MapGet("/api/books", async (HttpContext http, IAuthService authService, IBookService bookService) =>
            await WithUserAsync(http, authService, async user =>
            {
                var books = await bookService.GetUserBooksAsync(user.Id);
                return Results.Ok(books);
            }));

        // Upload a book via multipart/form-data or Base64 JSON payload
        app.MapPost("/api/books", async (HttpContext http, IAuthService authService, IBookService bookService) =>
            await WithUserAsync(http, authService, async user =>
            {
                // Handle multipart form upload
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
                    // Handle JSON upload with Base64-encoded bytes
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
            }));

        // Get book details by ID
        app.MapGet("/api/books/{id:guid}", async (Guid id, HttpContext http, IAuthService authService, IBookService bookService) =>
            await WithUserAsync(http, authService, async user =>
            {
                var result = await bookService.GetBookDetailAsync(user.Id, id);
                return ToHttpResult(result);
            }));

        // Get a specific page of a book
        app.MapGet("/api/books/{id:guid}/pages/{pageNumber:int}", async (
            Guid id,
            int pageNumber,
            HttpContext http,
            IAuthService authService,
            IBookService bookService) =>
            await WithUserAsync(http, authService, async user =>
            {
                var result = await bookService.GetBookPageAsync(user.Id, id, pageNumber);
                return ToHttpResult(result);
            }));

        // Update reading progress with Last-Write-Wins semantics
        app.MapPut("/api/books/{id:guid}/progress", async (
            Guid id,
            UpdateProgressRequest request,
            HttpContext http,
            IAuthService authService,
            IBookService bookService) =>
            await WithUserAsync(http, authService, async user =>
            {
                var result = await bookService.UpdateReadingProgressAsync(
                    user.Id,
                    id,
                    request.PageNumber,
                    request.ClientTimestampUtc,
                    request.ZoomLevel);

                return ToHttpResult(result);
            }));

        // Add a bookmark to a book
        app.MapPost("/api/books/{id:guid}/bookmarks", async (
            Guid id,
            CreateBookmarkRequest request,
            HttpContext http,
            IAuthService authService,
            IBookService bookService) =>
            await WithUserAsync(http, authService, async user =>
            {
                var result = await bookService.AddBookmarkAsync(user.Id, id, request.Name, request.PageNumber);
                return ToHttpResult(result);
            }));

        // Delete a bookmark by ID
        app.MapDelete("/api/books/{id:guid}/bookmarks/{bookmarkId:guid}", async (
            Guid id,
            Guid bookmarkId,
            HttpContext http,
            IAuthService authService,
            IBookService bookService) =>
            await WithUserAsync(http, authService, async user =>
            {
                var result = await bookService.DeleteBookmarkAsync(user.Id, id, bookmarkId);
                return ToHttpResult(result);
            }));

        // Delete a book by ID
        app.MapDelete("/api/books/{id:guid}", async (
            Guid id,
            HttpContext http,
            IAuthService authService,
            IBookService bookService) =>
            await WithUserAsync(http, authService, async user =>
            {
                var result = await bookService.DeleteBookAsync(user.Id, id);
                return ToHttpResult(result);
            }));

        // Download or stream original book file
        app.MapGet("/api/books/{id:guid}/file", async (
            Guid id,
            HttpContext http,
            IAuthService authService,
            IBookService bookService) =>
            await WithUserAsync(http, authService, async user =>
            {
                var result = await bookService.GetBookFileAsync(user.Id, id);
                if (!result.IsSuccess)
                {
                    return ToHttpResult(result);
                }

                // Return file with content type and byte-range support
                return Results.File(
                    result.Data.FileBytes,
                    contentType: string.Equals(result.Data.Format, "PDF", StringComparison.OrdinalIgnoreCase)
                        ? "application/pdf"
                        : "application/epub+zip",
                    enableRangeProcessing: true);
            }));
    }

    /// <summary>
    /// Executes an endpoint action within an authenticated user context or returns 401 Unauthorized.
    /// </summary>
    private static async Task<IResult> WithUserAsync(
        HttpContext http,
        IAuthService authService,
        Func<UserDto, Task<IResult>> action)
    {
        var user = await AuthenticateAsync(http, authService);
        if (user is null)
        {
            return UnauthorizedResult();
        }

        return await action(user);
    }

    /// <summary>
    /// Extracts the authentication token from the Authorization header or fallback 'token' query parameter.
    /// </summary>
    private static string? GetAuthHeader(HttpContext http)
    {
        var authHeader = http.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            return authHeader;
        }

        var tokenQuery = http.Request.Query["token"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(tokenQuery))
        {
            return tokenQuery;
        }

        return null;
    }

    /// <summary>
    /// Validates the bearer token in the current HTTP request.
    /// </summary>
    private static async Task<UserDto?> AuthenticateAsync(HttpContext http, IAuthService authService)
    {
        var token = GetAuthHeader(http);
        return await authService.ValidateTokenAsync(token);
    }

    /// <summary>
    /// Creates a standardized 401 Unauthorized JSON error response.
    /// </summary>
    private static IResult UnauthorizedResult() =>
        Results.Json(
            new ApiErrorResponse("Authentication required to perform this action.", EbookErrorCodes.Unauthorized),
            statusCode: 401);

    /// <summary>
    /// Converts a <see cref="ServiceResult{T}"/> into a standard ASP.NET Core JSON HTTP result.
    /// </summary>
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
