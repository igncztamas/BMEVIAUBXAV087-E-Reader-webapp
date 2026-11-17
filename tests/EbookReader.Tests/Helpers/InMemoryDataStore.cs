using EbookReader.Backend.Models;
using EbookReader.Backend.Services;

namespace EbookReader.Tests.Helpers;

/// <summary>
/// Thread-safe in-memory <see cref="IDataStore"/> implementation for isolated unit tests.
/// </summary>
internal sealed class InMemoryDataStore : IDataStore
{
    private readonly object _syncRoot = new();
    private readonly DatabaseSnapshot _snapshot = new();

    public Task<TResult> ReadAsync<TResult>(Func<DatabaseSnapshot, TResult> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_syncRoot)
        {
            return Task.FromResult(query(_snapshot));
        }
    }

    public Task<TResult> WriteAsync<TResult>(Func<DatabaseSnapshot, TResult> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        lock (_syncRoot)
        {
            return Task.FromResult(mutator(_snapshot));
        }
    }
}
