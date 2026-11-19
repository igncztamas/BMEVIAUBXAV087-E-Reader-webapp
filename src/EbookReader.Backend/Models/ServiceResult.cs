namespace EbookReader.Backend.Models;

/// <summary>
/// Represents a standardized result wrapper for service operations containing data or error details.
/// </summary>
/// <typeparam name="T">The payload type returned on success.</typeparam>
public class ServiceResult<T>
{
    /// <summary>Indicates whether the operation succeeded.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>The associated HTTP status code (e.g. 200, 201, 400, 403, 404, 409).</summary>
    public int StatusCode { get; init; }

    /// <summary>Descriptive human-readable error message on failure.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Standard error code from <see cref="EbookErrorCodes"/> on failure.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>The output payload when the operation is successful.</summary>
    public T? Data { get; init; }

    /// <summary>
    /// Creates a successful result instance with the specified data and status code.
    /// </summary>
    /// <param name="data">The payload.</param>
    /// <param name="statusCode">The HTTP status code (defaults to 200 OK).</param>
    public static ServiceResult<T> Success(T data, int statusCode = 200) => new()
    {
        IsSuccess = true,
        StatusCode = statusCode,
        Data = data
    };

    /// <summary>
    /// Creates a failure result instance with HTTP status code, error code, and message.
    /// </summary>
    /// <param name="statusCode">The HTTP status code (e.g. 400, 403, 404, 409).</param>
    /// <param name="errorCode">Immutable error code constant.</param>
    /// <param name="errorMessage">Detailed explanation of the error.</param>
    public static ServiceResult<T> Failure(int statusCode, string errorCode, string errorMessage) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}
