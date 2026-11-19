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

    /// <summary>
    /// Executes a read-only query against the in-memory <see cref="DatabaseSnapshot"/> under a lock.
    /// </summary>
    public Task<TResult> ReadAsync<TResult>(Func<DatabaseSnapshot, TResult> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Synchronize snapshot reads so concurrent test tasks observe consistent state.
        lock (_syncRoot)
        {
            return Task.FromResult(query(_snapshot));
        }
    }

    /// <summary>
    /// Executes a mutating operation against the in-memory <see cref="DatabaseSnapshot"/> under a lock.
    /// </summary>
    public Task<TResult> WriteAsync<TResult>(Func<DatabaseSnapshot, TResult> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        // Serialize snapshot mutations to mirror atomic store writes.
        lock (_syncRoot)
        {
            return Task.FromResult(mutator(_snapshot));
        }
    }
}
