using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EbookReader.Backend.Models;
using EbookReader.Backend.Services;

namespace EbookReader.Tests.Helpers;

/// <summary>
/// Routes frontend HttpClient calls directly to in-memory backend services for integration testing.
/// </summary>
internal sealed class InMemoryBackendHttpMessageHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly IAuthService _auth;
    private readonly IBookService _books;

    public InMemoryBackendHttpMessageHandler(IAuthService auth, IBookService books)
    {
        _auth = auth;
        _books = books;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "/";
        var method = request.Method.Method;
        var authHeader = request.Headers.Authorization?.ToString();

        var authResponse = await TryHandleAuthAsync(request, method, path, authHeader, cancellationToken);
        if (authResponse is not null)
        {
            return authResponse;
        }

        var user = await _auth.ValidateTokenAsync(authHeader);
        if (user is null)
        {
            return ToResponse(ServiceResult<object>.Failure(401, EbookErrorCodes.Unauthorized, "Login required."));
        }

        return await HandleBookEndpointsAsync(request, user.Id, method, path, cancellationToken);
    }

    private async Task<HttpResponseMessage?> TryHandleAuthAsync(
        HttpRequestMessage request,
        string method,
        string path,
        string? authHeader,
        CancellationToken cancellationToken)
    {
        if (method != "POST")
        {
            return null;
        }

        if (path == "/api/auth/register")
        {
            var body = await request.Content!.ReadFromJsonAsync<RegisterRequest>(JsonOpts, cancellationToken);
            return ToResponse(await _auth.RegisterAsync(body?.Username, body?.Password));
        }

        if (path == "/api/auth/login")
        {
            var body = await request.Content!.ReadFromJsonAsync<LoginRequest>(JsonOpts, cancellationToken);
            return ToResponse(await _auth.LoginAsync(body?.Username, body?.Password));
        }

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

    private async Task<HttpResponseMessage> HandleBookEndpointsAsync(
        HttpRequestMessage request,
        Guid userId,
        string method,
        string path,
        CancellationToken cancellationToken)
    {
        if (method == "GET" && path == "/api/books")
        {
            return ToResponse(ServiceResult<List<BookSummaryDto>>.Success(await _books.GetUserBooksAsync(userId)));
        }

        if (method == "POST" && path == "/api/books")
        {
            var body = await request.Content!.ReadFromJsonAsync<UploadBookRequest>(JsonOpts, cancellationToken);
            var bytes = Convert.FromBase64String(body!.FileBytesBase64);
            return ToResponse(await _books.UploadBookAsync(userId, body.FileName, bytes, body.Title));
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || segments[0] != "api" || segments[1] != "books" || !Guid.TryParse(segments[2], out var bookId))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

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

    private async Task<HttpResponseMessage> UpdateProgressAsync(
        HttpRequestMessage request,
        Guid userId,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadFromJsonAsync<UpdateProgressRequest>(JsonOpts, cancellationToken);
        return ToResponse(await _books.UpdateReadingProgressAsync(userId, bookId, body!.PageNumber, body.ClientTimestampUtc, body.ZoomLevel));
    }

    private async Task<HttpResponseMessage> AddBookmarkAsync(
        HttpRequestMessage request,
        Guid userId,
        Guid bookId,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadFromJsonAsync<CreateBookmarkRequest>(JsonOpts, cancellationToken);
        return ToResponse(await _books.AddBookmarkAsync(userId, bookId, body!.Name, body.PageNumber));
    }

    private static HttpResponseMessage ToResponse<T>(ServiceResult<T> result)
    {
        var content = result.IsSuccess
            ? JsonContent.Create(result.Data)
            : JsonContent.Create(new ApiErrorResponse(result.ErrorMessage ?? "Error", result.ErrorCode ?? "ERR"));

        return new HttpResponseMessage((HttpStatusCode)result.StatusCode) { Content = content };
    }
}
