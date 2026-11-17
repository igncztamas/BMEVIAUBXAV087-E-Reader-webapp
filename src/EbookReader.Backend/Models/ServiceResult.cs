namespace EbookReader.Backend.Models;

public class ServiceResult<T>
{
    public bool IsSuccess { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public T? Data { get; init; }

    public static ServiceResult<T> Success(T data, int statusCode = 200) => new()
    {
        IsSuccess = true,
        StatusCode = statusCode,
        Data = data
    };

    public static ServiceResult<T> Failure(int statusCode, string errorCode, string errorMessage) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}
