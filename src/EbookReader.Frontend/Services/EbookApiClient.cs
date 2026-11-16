using System.Net.Http.Headers;
using System.Net.Http.Json;
using EbookReader.Frontend.Models;

namespace EbookReader.Frontend.Services;

public interface IEbookApiClient
{
    Task<ApiCallResult<AuthResponseDto>> RegisterAsync(string username, string password);
    Task<ApiCallResult<AuthResponseDto>> LoginAsync(string username, string password);
    Task LogoutAsync();
    Task<ApiCallResult<List<BookSummaryItemDto>>> GetBooksAsync();
    Task<ApiCallResult<BookDetailItemDto>> UploadBookAsync(string fileName, byte[] fileBytes, string? title = null);
    Task<ApiCallResult<BookDetailItemDto>> GetBookDetailAsync(Guid bookId);
    Task<ApiCallResult<BookPageItemDto>> GetBookPageAsync(Guid bookId, int pageNumber);
    Task<ApiCallResult<UpdateProgressResponseDto>> UpdateProgressAsync(Guid bookId, int pageNumber, DateTimeOffset? clientTimestampUtc = null);
    Task<ApiCallResult<BookmarkItemDto>> AddBookmarkAsync(Guid bookId, string name, int pageNumber);
    Task<ApiCallResult<bool>> DeleteBookmarkAsync(Guid bookId, Guid bookmarkId);
    Task<ApiCallResult<bool>> DeleteBookAsync(Guid bookId);
}

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

    public async Task<ApiCallResult<List<BookSummaryItemDto>>> GetBooksAsync()
    {
        using var req = CreateAuthorizedRequest(HttpMethod.Get, "/api/books");
        var response = await _httpClient.SendAsync(req);
        return await ReadResponseAsync<List<BookSummaryItemDto>>(response);
    }

    public async Task<ApiCallResult<BookDetailItemDto>> UploadBookAsync(string fileName, byte[] fileBytes, string? title = null)
    {
        using var req = CreateAuthorizedRequest(HttpMethod.Post, "/api/books");
        var payload = new UploadBookRequestDto(
            fileName,
            Convert.ToBase64String(fileBytes),
            title);
        req.Content = JsonContent.Create(payload);
        var response = await _httpClient.SendAsync(req);
        return await ReadResponseAsync<BookDetailItemDto>(response);
    }

    public async Task<ApiCallResult<BookDetailItemDto>> GetBookDetailAsync(Guid bookId)
    {
        using var req = CreateAuthorizedRequest(HttpMethod.Get, $"/api/books/{bookId}");
        var response = await _httpClient.SendAsync(req);
        return await ReadResponseAsync<BookDetailItemDto>(response);
    }

    public async Task<ApiCallResult<BookPageItemDto>> GetBookPageAsync(Guid bookId, int pageNumber)
    {
        using var req = CreateAuthorizedRequest(HttpMethod.Get, $"/api/books/{bookId}/pages/{pageNumber}");
        var response = await _httpClient.SendAsync(req);
        return await ReadResponseAsync<BookPageItemDto>(response);
    }

    public async Task<ApiCallResult<UpdateProgressResponseDto>> UpdateProgressAsync(
        Guid bookId,
        int pageNumber,
        DateTimeOffset? clientTimestampUtc = null)
    {
        using var req = CreateAuthorizedRequest(HttpMethod.Put, $"/api/books/{bookId}/progress");
        req.Content = JsonContent.Create(new UpdateProgressRequestDto(pageNumber, clientTimestampUtc ?? DateTimeOffset.UtcNow));
        var response = await _httpClient.SendAsync(req);
        return await ReadResponseAsync<UpdateProgressResponseDto>(response);
    }

    public async Task<ApiCallResult<BookmarkItemDto>> AddBookmarkAsync(Guid bookId, string name, int pageNumber)
    {
        using var req = CreateAuthorizedRequest(HttpMethod.Post, $"/api/books/{bookId}/bookmarks");
        req.Content = JsonContent.Create(new CreateBookmarkRequestDto(name, pageNumber));
        var response = await _httpClient.SendAsync(req);
        return await ReadResponseAsync<BookmarkItemDto>(response);
    }

    public async Task<ApiCallResult<bool>> DeleteBookmarkAsync(Guid bookId, Guid bookmarkId)
    {
        using var req = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/books/{bookId}/bookmarks/{bookmarkId}");
        var response = await _httpClient.SendAsync(req);
        return await ReadResponseAsync<bool>(response);
    }

    public async Task<ApiCallResult<bool>> DeleteBookAsync(Guid bookId)
    {
        using var req = CreateAuthorizedRequest(HttpMethod.Delete, $"/api/books/{bookId}");
        var response = await _httpClient.SendAsync(req);
        return await ReadResponseAsync<bool>(response);
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
