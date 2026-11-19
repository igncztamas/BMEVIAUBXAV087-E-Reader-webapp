using System.Net.Sockets;
using EbookReader.Backend.Services;
using EbookReader.Frontend.Components;
using EbookReader.Frontend.Services;

// Configure web application builder
var builder = WebApplication.CreateBuilder(args);

// Check whether backend port 5050 is already occupied by an external service process
bool isBackendPortAlreadyInUse = IsPortInUse("127.0.0.1", 5050);
builder.WebHost.UseUrls(isBackendPortAlreadyInUse
    ? ["http://localhost:5060"]
    : ["http://localhost:5060", "http://localhost:5050"]);

// Locate the SQLite database persistence file
var dataFilePath = builder.Configuration["DataStore:FilePath"]
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "EbookReader.Backend", "App_Data", "ebook_store.db"));

// Register backend storage, encryption, validation, and parsing services in this host
builder.Services.AddEbookBackendServices(dataFilePath);

// Configure Blazor Interactive Server services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure base URL for backend REST API calls
var backendBaseUrl = builder.Configuration["BackendBaseUrl"] ?? "http://localhost:5050";

// Register user session service and API client
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

// Configure HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

// Map backend API endpoints (/api/auth/*, /api/books/*) directly onto the web host
EbookApiEndpoints.MapEndpoints(app);

// Map Blazor root component with interactive server rendering
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>
/// Probes whether a specific host and TCP port are actively accepting connections.
/// </summary>
/// <param name="host">Target IP address or host name.</param>
/// <param name="port">Target TCP port number.</param>
/// <returns>True if the port is currently bound and listening; false otherwise.</returns>
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
        // Connection refused or timed out means port is available
        return false;
    }
}
