using EbookReader.Backend.Services;

// Create and configure the ASP.NET Core web application builder.
var builder = WebApplication.CreateBuilder(args);

// Resolve SQLite database file path from configuration, falling back to App_Data/ebook_store.db.
var dataFilePath = builder.Configuration["DataStore:FilePath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "ebook_store.db");

// Register backend services (data store, validators, auth service, book service).
builder.Services.AddEbookBackendServices(dataFilePath);

// Configure permissive Cross-Origin Resource Sharing (CORS) policy for API consumers.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Build the web application host.
var app = builder.Build();

// Enable CORS middleware.
app.UseCors();

// Map all Minimal API endpoints.
EbookApiEndpoints.MapEndpoints(app);

// Run the application.
app.Run();
