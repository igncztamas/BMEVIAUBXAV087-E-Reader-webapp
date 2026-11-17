namespace EbookReader.Frontend.Models;

public class ApiCallResult<T>
{
    public bool IsSuccess { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public T? Data { get; init; }

    public static ApiCallResult<T> Success(T data, int statusCode = 200) => new()
    {
        IsSuccess = true,
        StatusCode = statusCode,
        Data = data
    };

    public static ApiCallResult<T> Failure(int statusCode, string errorMessage, string? errorCode = null) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        ErrorMessage = errorMessage,
        ErrorCode = errorCode
    };
}
