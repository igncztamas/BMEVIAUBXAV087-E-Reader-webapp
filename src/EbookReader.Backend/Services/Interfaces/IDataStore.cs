namespace EbookReader.Backend.Services;

using EbookReader.Backend.Models;

/// <summary>
/// Thread-safe storage interface providing transactional read and write access to database snapshots.
/// </summary>
public interface IDataStore
{
    /// <summary>
    /// Executes a thread-safe read-only query against the current database snapshot.
    /// </summary>
    /// <typeparam name="TResult">The result type returned by the query.</typeparam>
    /// <param name="query">A delegate executing against the in-memory snapshot.</param>
    /// <returns>The query result.</returns>
    Task<TResult> ReadAsync<TResult>(Func<DatabaseSnapshot, TResult> query);

    /// <summary>
    /// Executes a thread-safe mutating operation against the database snapshot and persists changes.
    /// </summary>
    /// <typeparam name="TResult">The result type returned by the mutator.</typeparam>
    /// <param name="mutator">A delegate mutating the database snapshot.</param>
    /// <returns>The mutator result.</returns>
    Task<TResult> WriteAsync<TResult>(Func<DatabaseSnapshot, TResult> mutator);
}
