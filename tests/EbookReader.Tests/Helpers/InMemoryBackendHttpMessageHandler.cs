using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EbookReader.Backend.Models;
using EbookReader.Backend.Services;

namespace EbookReader.Tests.Helpers;

/// <summary>
/// Routes frontend <see cref="HttpClient"/> calls directly to in-memory backend services for integration testing.
/// </summary>
internal sealed class InMemoryBackendHttpMessageHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly IAuthService _auth;
    private readonly IBookService _books;

    /// <summary>
    /// Initializes the handler with the backend authentication and book services under test.
    /// </summary>
    public InMemoryBackendHttpMessageHandler(IAuthService auth, IBookService books)
    {
        _auth = auth;
        _books = books;
    }

    /// <summary>
    /// Intercepts outgoing HTTP requests, enforces token authorization on protected routes, and dispatches to backend services.
    /// </summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "/";
        var method = request.Method.Method;
        var authHeader = request.Headers.Authorization?.ToString();

        // Public authentication endpoints do not require an active bearer session.
        var authResponse = await TryHandleAuthAsync(request, method, path, authHeader, cancellationToken);
        if (authResponse is not null)
        {
            return authResponse;
        }

        // Enforce session authorization on all library, reader, and bookmark endpoints.
        var user = await _auth.ValidateTokenAsync(authHeader);
        if (user is null)
        {
            return ToResponse(ServiceResult<object>.Failure(401, EbookErrorCodes.Unauthorized, "Login required."));
        }

        return await HandleBookEndpointsAsync(request, user.Id, method, path, cancellationToken);
    }

    /// <summary>
    /// Handles <c>/api/auth/*</c> routes and returns <see langword="null"/> when the request targets another route group.
    /// </summary>
    private async Task<HttpResponseMessage?> TryHandleAuthAsync(
        HttpRequestMessage request,
        string method,
        string path,
        string? authHeader,
        CancellationToken cancellationToken)
    {
        // Only POST requests are valid for register, login, and logout endpoints.
        if (method != "POST")
        {
            return null;
        }

        // Route: POST /api/auth/register
        if (path == "/api/auth/register")
        {
            var body = await request.Content!.ReadFromJsonAsync<RegisterRequest>(JsonOpts, cancellationToken);
            return ToResponse(await _auth.RegisterAsync(body?.Username, body?.Password));
        }

        // Route: POST /api/auth/login
        if (path == "/api/auth/login")
        {
            var body = await request.Content!.ReadFromJsonAsync<LoginRequest>(JsonOpts, cancellationToken);
            return ToResponse(await _auth.LoginAsync(body?.Username, body?.Password));
        }

        // Route: POST /api/auth/logout
        if (path == "/api/auth/logout")
        {
            await _auth.LogoutAsync(authHeader);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { message = "OK" })
            };
        }

        return null;
    }

    /// <summary>
    /// Dispatches authenticated <c>/api/books/*</c> requests to <see cref="IBookService"/>.
    /// </summary>
    private async Task<HttpResponseMessage> HandleBookEndpointsAsync(
        HttpRequestMessage request,
        Guid userId,
        string method,
        string path,
        CancellationToken cancellationToken)
    {
        // Route: GET /api/books (list user's library)
        if (method == "GET" && path == "/api/books")
        {
            return ToResponse(ServiceResult<List<BookSummaryDto>>.Success(await _books.GetUserBooksAsync(userId)));
        }

        // Route: POST /api/books (upload base64-encoded PDF/EPUB file)
        if (method == "POST" && path == "/api/books")
        {
            var body = await request.Content!.ReadFromJsonAsync<UploadBookRequest>(JsonOpts, cancellationToken);
            var bytes = Convert.FromBase64String(body!.FileBytesBase64);
            return ToResponse(await _books.UploadBookAsync(userId, body.FileName, bytes, body.Title));
        }

        // Parse book-scoped resource paths: /api/books/{bookId}/...
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || segments[0] != "api" || segments[1] != "books" || !Guid.TryParse(segments[2], out var bookId))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        // Match remaining book detail, page, progress, and bookmark sub-routes.
        return segments switch
        {
            [_, _, _] when method == "GET" => ToResponse(await _books.GetBookDetailAsync(userId, bookId)),
            [_, _, _] when method == "DELETE" => ToResponse(await _books.DeleteBookAsync(userId, bookId)),
            [_, _, _, "pages", var p] when int.TryParse(p, out var pageNum) =>
                ToResponse(await _books.GetBookPageAsync(userId, bookId, pageNum)),
            [_, _, _, "progress"] when method == "PUT" => await UpdateProgressAsync(request, userId, bookId, cancellationToken),
            [_, _, _, "bookmarks"] when method == "POST" => await AddBookmarkAsync(request, userId, bookId, cancellationToken),
            [_, _, _, "bookmarks", var bmId] when method == "DELETE" && Guid.TryParse(bmId, out var bookmarkId) =>
                ToResponse(await _books.DeleteBookmarkAsync(userId, bookId, bookmarkId)),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }

    /// <summary>
    /// Handles <c>PUT /api/books/{bookId}/progress</c> including page, timestamp, and zoom level.
    /// </summary>
    private async Task<HttpResponseMessage> UpdateProgressAsync(
        HttpRequestMessage request,
        Guid userId,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadFromJsonAsync<UpdateProgressRequest>(JsonOpts, cancellationToken);
        return ToResponse(await _books.UpdateReadingProgressAsync(userId, bookId, body!.PageNumber, body.ClientTimestampUtc, body.ZoomLevel));
    }

    /// <summary>
    /// Handles <c>POST /api/books/{bookId}/bookmarks</c> to create a named page bookmark.
    /// </summary>
    private async Task<HttpResponseMessage> AddBookmarkAsync(
        HttpRequestMessage request,
        Guid userId,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadFromJsonAsync<CreateBookmarkRequest>(JsonOpts, cancellationToken);
        return ToResponse(await _books.AddBookmarkAsync(userId, bookId, body!.Name, body.PageNumber));
    }

    /// <summary>
    /// Converts a <see cref="ServiceResult{T}"/> into an HTTP JSON response matching the backend API contract.
    /// </summary>
    private static HttpResponseMessage ToResponse<T>(ServiceResult<T> result)
    {
        // Serialize Data on success or ApiErrorResponse on failure.
        var content = result.IsSuccess
            ? JsonContent.Create(result.Data)
            : JsonContent.Create(new ApiErrorResponse(result.ErrorMessage ?? "Error", result.ErrorCode ?? "ERR"));

        return new HttpResponseMessage((HttpStatusCode)result.StatusCode) { Content = content };
    }
}
