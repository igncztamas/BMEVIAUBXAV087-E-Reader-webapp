using System.Net.Http.Headers;
using System.Net.Http.Json;
using EbookReader.Frontend.Models;

namespace EbookReader.Frontend.Services;

public class EbookApiClient : IEbookApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IUserSessionService _session;

    public EbookApiClient(HttpClient httpClient, IUserSessionService session)
    {
        _httpClient = httpClient;
        _session = session;
    }

    public async Task<ApiCallResult<AuthResponseDto>> RegisterAsync(string username, string password)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/auth/register", new RegisterRequestDto(username, password));
        var result = await ReadResponseAsync<AuthResponseDto>(response);
        if (result.IsSuccess && result.Data is not null)
        {
            _session.SetSession(result.Data.Token, result.Data.User);
        }
        return result;
    }

    public async Task<ApiCallResult<AuthResponseDto>> LoginAsync(string username, string password)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new LoginRequestDto(username, password));
        var result = await ReadResponseAsync<AuthResponseDto>(response);
        if (result.IsSuccess && result.Data is not null)
        {
            _session.SetSession(result.Data.Token, result.Data.User);
        }
        return result;
    }

    public async Task LogoutAsync()
    {
        try
        {
            using var req = CreateAuthorizedRequest(HttpMethod.Post, "/api/auth/logout");
            await _httpClient.SendAsync(req);
        }
        catch
        {
        }
        finally
        {
            _session.ClearSession();
        }
    }

    public Task<ApiCallResult<List<BookSummaryItemDto>>> GetBooksAsync() =>
        SendAsync<List<BookSummaryItemDto>>(HttpMethod.Get, "/api/books");

    public Task<ApiCallResult<BookDetailItemDto>> UploadBookAsync(string fileName, byte[] fileBytes, string? title = null)
    {
        var payload = new UploadBookRequestDto(
            fileName,
            Convert.ToBase64String(fileBytes),
            title);
        return SendAsync<BookDetailItemDto>(HttpMethod.Post, "/api/books", payload);
    }

    public Task<ApiCallResult<BookDetailItemDto>> GetBookDetailAsync(Guid bookId) =>
        SendAsync<BookDetailItemDto>(HttpMethod.Get, $"/api/books/{bookId}");

    public Task<ApiCallResult<BookPageItemDto>> GetBookPageAsync(Guid bookId, int pageNumber) =>
        SendAsync<BookPageItemDto>(HttpMethod.Get, $"/api/books/{bookId}/pages/{pageNumber}");

    public Task<ApiCallResult<UpdateProgressResponseDto>> UpdateProgressAsync(
        Guid bookId,
        int pageNumber,
        DateTimeOffset? clientTimestampUtc = null,
        int? zoomLevel = null)
    {
        var payload = new UpdateProgressRequestDto(pageNumber, clientTimestampUtc ?? DateTimeOffset.UtcNow, zoomLevel);
        return SendAsync<UpdateProgressResponseDto>(HttpMethod.Put, $"/api/books/{bookId}/progress", payload);
    }

    public Task<ApiCallResult<BookmarkItemDto>> AddBookmarkAsync(Guid bookId, string name, int pageNumber) =>
        SendAsync<BookmarkItemDto>(HttpMethod.Post, $"/api/books/{bookId}/bookmarks", new CreateBookmarkRequestDto(name, pageNumber));

    public Task<ApiCallResult<bool>> DeleteBookmarkAsync(Guid bookId, Guid bookmarkId) =>
        SendAsync<bool>(HttpMethod.Delete, $"/api/books/{bookId}/bookmarks/{bookmarkId}");

    public Task<ApiCallResult<bool>> DeleteBookAsync(Guid bookId) =>
        SendAsync<bool>(HttpMethod.Delete, $"/api/books/{bookId}");

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

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string relativeUrl)
    {
        var req = new HttpRequestMessage(method, relativeUrl);
        if (!string.IsNullOrWhiteSpace(_session.Token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Token);
        }
        return req;
    }

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
                error?.Error ?? $"Server error ({statusCode}).",
                error?.Code);
        }
        catch
        {
            return ApiCallResult<T>.Failure(statusCode, $"Server error ({statusCode}).");
        }
    }
}
