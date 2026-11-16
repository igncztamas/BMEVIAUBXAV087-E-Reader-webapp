using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EbookReader.Backend.Models;
using EbookReader.Backend.Services;
using EbookReader.Frontend.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EbookReader.Tests;

[TestClass]
public class FrontendBackendIntegrationTests
{
    private sealed class InMemoryBackendHttpMessageHandler : HttpMessageHandler
    {
        private readonly IAuthService _auth;
        private readonly IBookService _books;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

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

            if (method == "POST" && path == "/api/auth/register")
            {
                var body = await request.Content!.ReadFromJsonAsync<RegisterRequest>(JsonOpts, cancellationToken);
                return ToResponse(await _auth.RegisterAsync(body?.Username, body?.Password));
            }

            if (method == "POST" && path == "/api/auth/login")
            {
                var body = await request.Content!.ReadFromJsonAsync<LoginRequest>(JsonOpts, cancellationToken);
                return ToResponse(await _auth.LoginAsync(body?.Username, body?.Password));
            }

            if (method == "POST" && path == "/api/auth/logout")
            {
                await _auth.LogoutAsync(authHeader);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { message = "OK" })
                };
            }

            var user = await _auth.ValidateTokenAsync(authHeader);
            if (user is null)
            {
                return ToResponse(ServiceResult<object>.Failure(401, "UNAUTHORIZED", "Login required."));
            }

            if (method == "GET" && path == "/api/books")
            {
                var list = await _books.GetUserBooksAsync(user.Id);
                return ToResponse(ServiceResult<List<BookSummaryDto>>.Success(list));
            }

            if (method == "POST" && path == "/api/books")
            {
                var body = await request.Content!.ReadFromJsonAsync<UploadBookRequest>(JsonOpts, cancellationToken);
                var bytes = Convert.FromBase64String(body!.FileBytesBase64);
                return ToResponse(await _books.UploadBookAsync(user.Id, body.FileName, bytes, body.Title));
            }

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 3 && segments[0] == "api" && segments[1] == "books" && Guid.TryParse(segments[2], out var bookId))
            {
                if (method == "GET")
                {
                    return ToResponse(await _books.GetBookDetailAsync(user.Id, bookId));
                }
                if (method == "DELETE")
                {
                    return ToResponse(await _books.DeleteBookAsync(user.Id, bookId));
                }
            }

            if (segments.Length == 5 && segments[0] == "api" && segments[1] == "books" &&
                Guid.TryParse(segments[2], out var pageBookId) && segments[3] == "pages" && int.TryParse(segments[4], out var pageNum))
            {
                return ToResponse(await _books.GetBookPageAsync(user.Id, pageBookId, pageNum));
            }

            if (segments.Length == 4 && segments[0] == "api" && segments[1] == "books" &&
                Guid.TryParse(segments[2], out var progBookId) && segments[3] == "progress" && method == "PUT")
            {
                var body = await request.Content!.ReadFromJsonAsync<UpdateProgressRequest>(JsonOpts, cancellationToken);
                return ToResponse(await _books.UpdateReadingProgressAsync(user.Id, progBookId, body!.PageNumber, body.ClientTimestampUtc));
            }

            if (segments.Length == 4 && segments[0] == "api" && segments[1] == "books" &&
                Guid.TryParse(segments[2], out var bmBookId) && segments[3] == "bookmarks" && method == "POST")
            {
                var body = await request.Content!.ReadFromJsonAsync<CreateBookmarkRequest>(JsonOpts, cancellationToken);
                return ToResponse(await _books.AddBookmarkAsync(user.Id, bmBookId, body!.Name, body.PageNumber));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage ToResponse<T>(ServiceResult<T> result)
        {
            if (result.IsSuccess)
            {
                return new HttpResponseMessage((HttpStatusCode)result.StatusCode)
                {
                    Content = JsonContent.Create(result.Data)
                };
            }

            return new HttpResponseMessage((HttpStatusCode)result.StatusCode)
            {
                Content = JsonContent.Create(new ApiErrorResponse(result.ErrorMessage ?? "Error", result.ErrorCode ?? "ERR"))
            };
        }
    }

    [TestMethod]
    public async Task EndToEnd_FrontendClientAndBackendWorkflow()
    {
        IDataStore store = new JsonDataStore();
        IEbookValidatorAndParser validator = new EbookValidatorAndParser();
        IAuthService auth = new AuthService(store);
        IBookService books = new BookService(store, validator);

        IUserSessionService session = new UserSessionService();
        var httpClient = new HttpClient(new InMemoryBackendHttpMessageHandler(auth, books))
        {
            BaseAddress = new Uri("http://localhost:5050")
        };
        IEbookApiClient apiClient = new EbookApiClient(httpClient, session);

        Assert.IsFalse(session.IsAuthenticated);

        var reg = await apiClient.RegisterAsync("frontend_user", "Secret1234");
        Assert.IsTrue(reg.IsSuccess);
        Assert.IsTrue(session.IsAuthenticated);
        Assert.AreEqual("frontend_user", session.CurrentUser?.Username);

        var epubBytes = TestEbookFactory.CreateValidEpubBytes(
            "Frontend Test Book",
            "Chapter 1 text",
            "Chapter 2 text",
            "Chapter 3 text");

        var upload = await apiClient.UploadBookAsync("test.epub", epubBytes, "Frontend Test Book");
        Assert.IsTrue(upload.IsSuccess);
        var bookId = upload.Data!.Id;

        var page2 = await apiClient.GetBookPageAsync(bookId, 2);
        Assert.IsTrue(page2.IsSuccess);
        StringAssert.Contains(page2.Data!.Content, "Chapter 2 text");

        var progress = await apiClient.UpdateProgressAsync(bookId, 2);
        Assert.IsTrue(progress.IsSuccess);
        Assert.AreEqual(2, progress.Data!.CurrentPage);

        var bookmark = await apiClient.AddBookmarkAsync(bookId, "Second Chapter", 2);
        Assert.IsTrue(bookmark.IsSuccess);

        await apiClient.LogoutAsync();
        Assert.IsFalse(session.IsAuthenticated);

        var login = await apiClient.LoginAsync("frontend_user", "Secret1234");
        Assert.IsTrue(login.IsSuccess);
        Assert.IsTrue(session.IsAuthenticated);

        var library = await apiClient.GetBooksAsync();
        Assert.IsTrue(library.IsSuccess);
        Assert.AreEqual(1, library.Data!.Count);
        Assert.AreEqual(2, library.Data[0].CurrentPage);
        Assert.AreEqual(1, library.Data[0].BookmarkCount);
    }
}
