using System.Text.Json;
using EbookReader.Backend.Models;

namespace EbookReader.Backend.Services;

public class JsonDataStore : IDataStore
{
    private readonly string? _filePath;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private DatabaseSnapshot _snapshot = new();
    private bool _loaded;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonDataStore(string? filePath = null)
    {
        _filePath = filePath;
    }

    public async Task<TResult> ReadAsync<TResult>(Func<DatabaseSnapshot, TResult> query)
    {
        await _mutex.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            return query(_snapshot);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<TResult> WriteAsync<TResult>(Func<DatabaseSnapshot, TResult> mutator)
    {
        await _mutex.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            var result = mutator(_snapshot);
            await PersistAsync();
            return result;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_filePath) && File.Exists(_filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_filePath);
                var loaded = JsonSerializer.Deserialize<DatabaseSnapshot>(json, JsonOptions);
                if (loaded is not null)
                {
                    _snapshot = loaded;
                }
            }
            catch
            {
                _snapshot = new DatabaseSnapshot();
            }
        }

        _loaded = true;
    }

    private async Task PersistAsync()
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_snapshot, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
