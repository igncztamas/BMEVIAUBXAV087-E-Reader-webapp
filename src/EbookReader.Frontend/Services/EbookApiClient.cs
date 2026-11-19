using System.Net.Http.Headers;
using System.Net.Http.Json;
using EbookReader.Frontend.Models;

namespace EbookReader.Frontend.Services;

/// <summary>
/// Communicates with backend authentication and e-book endpoints,
/// automatically attaching authorization headers and serializing requests.
/// </summary>
public class EbookApiClient : IEbookApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IUserSessionService _session;

    /// <summary>
    /// Initializes a new instance of <see cref="EbookApiClient"/>.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client instance.</param>
    /// <param name="session">User session service supplying current token.</param>
    public EbookApiClient(HttpClient httpClient, IUserSessionService session)
    {
        _httpClient = httpClient;
        _session = session;
    }

    /// <inheritdoc/>
    public async Task<ApiCallResult<AuthResponseDto>> RegisterAsync(string username, string password)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto(username, password));
        var result = await ReadResponseAsync<AuthResponseDto>(response);

        // On successful registration, automatically activate the user session
        if (result.IsSuccess && result.Data is not null)
        {
            _session.SetSession(result.Data.Token, result.Data.User);
        }
        return result;
    }

    /// <inheritdoc/>
    public async Task<ApiCallResult<AuthResponseDto>> LoginAsync(string username, string password)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new LoginRequestDto(username, password));
        var result = await ReadResponseAsync<AuthResponseDto>(response);

        // On successful login, automatically activate the user session
        if (result.IsSuccess && result.Data is not null)
        {
            _session.SetSession(result.Data.Token, result.Data.User);
        }
        return result;
    }

    /// <inheritdoc/>
    public async Task LogoutAsync()
    {
        try
        {
            using var req = CreateAuthorizedRequest(HttpMethod.Post, "/api/auth/logout");
            await _httpClient.SendAsync(req);
        }
        catch
        {
            // Network failures during logout should still clear the client session
        }
        finally
        {
            _session.ClearSession();
        }
    }

    /// <inheritdoc/>
    public Task<ApiCallResult<List<BookSummaryItemDto>>> GetBooksAsync() =>
        SendAsync<List<BookSummaryItemDto>>(HttpMethod.Get, "/api/books");

    /// <inheritdoc/>
    public Task<ApiCallResult<BookDetailItemDto>> UploadBookAsync(string fileName, byte[] fileBytes, string? title = null)
    {
        var payload = new UploadBookRequestDto(
            fileName,
            Convert.ToBase64String(fileBytes),
            title);
        return SendAsync<BookDetailItemDto>(HttpMethod.Post, "/api/books", payload);
    }

    /// <inheritdoc/>
    public Task<ApiCallResult<BookDetailItemDto>> GetBookDetailAsync(Guid bookId) =>
        SendAsync<BookDetailItemDto>(HttpMethod.Get, $"/api/books/{bookId}");

    /// <inheritdoc/>
    public Task<ApiCallResult<BookPageItemDto>> GetBookPageAsync(Guid bookId, int pageNumber) =>
        SendAsync<BookPageItemDto>(HttpMethod.Get, $"/api/books/{bookId}/pages/{pageNumber}");

    /// <inheritdoc/>
    public Task<ApiCallResult<UpdateProgressResponseDto>> UpdateProgressAsync(
        Guid bookId,
        int pageNumber,
        DateTimeOffset? clientTimestampUtc = null,
        int? zoomLevel = null)
    {
        var payload = new UpdateProgressRequestDto(pageNumber, clientTimestampUtc ?? DateTimeOffset.UtcNow, zoomLevel);
        return SendAsync<UpdateProgressResponseDto>(HttpMethod.Put, $"/api/books/{bookId}/progress", payload);
    }

    /// <inheritdoc/>
    public Task<ApiCallResult<BookmarkItemDto>> AddBookmarkAsync(Guid bookId, string name, int pageNumber) =>
        SendAsync<BookmarkItemDto>(HttpMethod.Post, $"/api/books/{bookId}/bookmarks", new CreateBookmarkRequestDto(name, pageNumber));

    /// <inheritdoc/>
    public Task<ApiCallResult<bool>> DeleteBookmarkAsync(Guid bookId, Guid bookmarkId) =>
        SendAsync<bool>(HttpMethod.Delete, $"/api/books/{bookId}/bookmarks/{bookmarkId}");

    /// <inheritdoc/>
    public Task<ApiCallResult<bool>> DeleteBookAsync(Guid bookId) =>
        SendAsync<bool>(HttpMethod.Delete, $"/api/books/{bookId}");

    /// <summary>
    /// Shared helper that creates an authorized request, serializes optional JSON content,
    /// sends the request, and formats the response.
    /// </summary>
    private async Task<ApiCallResult<T>> SendAsync<T>(HttpMethod method, string url, object? payload = null)
    {
        using var req = CreateAuthorizedRequest(method, url);
        if (payload is not null)
        {
            req.Content = JsonContent.Create(payload);
        }
        var response = await _httpClient.SendAsync(req);
        return await ReadResponseAsync<T>(response);
    }

    /// <summary>
    /// Creates an HttpRequestMessage attaching the Bearer authorization header if a token exists.
    /// </summary>
    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string relativeUrl)
    {
        var req = new HttpRequestMessage(method, relativeUrl);
        if (!string.IsNullOrWhiteSpace(_session.Token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Token);
        }
        return req;
    }

    /// <summary>
    /// Reads and deserializes an HTTP response, extracting either the payload data or structured ApiErrorDto.
    /// </summary>
    private static async Task<ApiCallResult<T>> ReadResponseAsync<T>(HttpResponseMessage response)
    {
        int statusCode = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            var data = await response.Content.ReadFromJsonAsync<T>();
            return ApiCallResult<T>.Success(data!, statusCode);
        }

        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiErrorDto>();
            return ApiCallResult<T>.Failure(
                statusCode,
                error?.ResolvedError ?? $"Server error ({statusCode}).",
                error?.ResolvedCode);
        }
        catch
        {
            // Handle plain-text or unexpected error responses gracefully
            return ApiCallResult<T>.Failure(statusCode, $"Server error ({statusCode}).");
        }
    }
}
