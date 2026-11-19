namespace EbookReader.Frontend.Models;

/// <summary>
/// Encapsulates the result of a frontend API call to the backend endpoints,
/// containing HTTP status codes, payload data, and structured error details.
/// </summary>
/// <typeparam name="T">The type of data returned by a successful API operation.</typeparam>
public class ApiCallResult<T>
{
    /// <summary>
    /// Gets a value indicating whether the API request completed successfully (HTTP 2xx).
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the HTTP response status code.
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// Gets the error message returned by the server or generated during request processing.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the optional machine-readable error code defined in EbookErrorCodes.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Gets the payload data returned by the backend when the call succeeded.
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// Creates a successful API call result container with the specified response data.
    /// </summary>
    /// <param name="data">The deserialized response payload.</param>
    /// <param name="statusCode">The HTTP status code (defaults to 200 OK).</param>
    /// <returns>A successful <see cref="ApiCallResult{T}"/> instance.</returns>
    public static ApiCallResult<T> Success(T data, int statusCode = 200) => new()
    {
        IsSuccess = true,
        StatusCode = statusCode,
        Data = data
    };

    /// <summary>
    /// Creates a failed API call result container with error information.
    /// </summary>
    /// <param name="statusCode">The HTTP status code returned by the server.</param>
    /// <param name="errorMessage">The human-readable description of the error.</param>
    /// <param name="errorCode">The optional EbookErrorCodes identifier.</param>
    /// <returns>A failed <see cref="ApiCallResult{T}"/> instance.</returns>
    public static ApiCallResult<T> Failure(int statusCode, string errorMessage, string? errorCode = null) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        ErrorMessage = errorMessage,
        ErrorCode = errorCode
    };
}
