using System.Net.Sockets;
using EbookReader.Frontend.Components;
using EbookReader.Frontend.Services;

var builder = WebApplication.CreateBuilder(args);

bool isBackendPortAlreadyInUse = IsPortInUse("127.0.0.1", 5050);
builder.WebHost.UseUrls(isBackendPortAlreadyInUse
    ? ["http://localhost:5060"]
    : ["http://localhost:5060", "http://localhost:5050"]);

var dataFilePath = builder.Configuration["DataStore:FilePath"]
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "EbookReader.Backend", "App_Data", "ebook_store.json"));

builder.Services.AddEbookBackendServices(dataFilePath);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var backendBaseUrl = builder.Configuration["BackendBaseUrl"] ?? "http://localhost:5050";

builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<IUserSessionService>(sp => sp.GetRequiredService<UserSessionService>());
builder.Services.AddScoped(sp =>
{
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri(backendBaseUrl)
    };
    var session = sp.GetRequiredService<IUserSessionService>();
    return new EbookApiClient(httpClient, session);
});
builder.Services.AddScoped<IEbookApiClient>(sp => sp.GetRequiredService<EbookApiClient>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

EbookApiEndpoints.MapEndpoints(app);

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static bool IsPortInUse(string host, int port)
{
    try
    {
        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(host, port);
        return connectTask.Wait(TimeSpan.FromMilliseconds(150)) && client.Connected;
    }
    catch
    {
        return false;
    }
}
