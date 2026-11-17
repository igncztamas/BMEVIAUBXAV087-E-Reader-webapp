using EbookReader.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

var dataFilePath = builder.Configuration["DataStore:FilePath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "ebook_store.db");

builder.Services.AddEbookBackendServices(dataFilePath);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();

EbookApiEndpoints.MapEndpoints(app);

app.Run();
