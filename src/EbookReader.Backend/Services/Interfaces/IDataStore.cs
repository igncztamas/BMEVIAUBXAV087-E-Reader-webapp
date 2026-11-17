using EbookReader.Backend.Models;

namespace EbookReader.Backend.Services;

public interface IDataStore
{
    Task<TResult> ReadAsync<TResult>(Func<DatabaseSnapshot, TResult> query);
    Task<TResult> WriteAsync<TResult>(Func<DatabaseSnapshot, TResult> mutator);
}
