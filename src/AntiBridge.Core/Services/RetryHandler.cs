using System.Net;

namespace AntiBridge.Core.Services;

/// <summary>
/// Configuration options for retry behavior on authentication errors.
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// Maximum retry attempts for 401 errors.
    /// Default is 1 to comply with requirement 2.5.
    /// </summary>
    public int MaxAuthRetries { get; set; } = 1;

    /// <summary>
    /// Enable automatic token refresh on 401.
    /// When enabled, the system will attempt to refresh the token before retrying.
    /// </summary>
    public bool AutoRefreshToken { get; set; } = true;
}

/// <summary>
/// Result of a retry operation.
/// </summary>
/// <typeparam name="T">The type of the result value</typeparam>
public class RetryResult<T>
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The result value if successful.
    /// </summary>
    public T? Value { get; init; }

    /// <summary>
    /// The exception if failed.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Number of retry attempts made.
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// Whether a token refresh was attempted.
    /// </summary>
    public bool TokenRefreshAttempted { get; init; }

    /// <summary>
    /// Whether the token refresh succeeded.
    /// </summary>
    public bool TokenRefreshSucceeded { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static RetryResult<T> Succeeded(T value, int retryCount, bool tokenRefreshAttempted, bool tokenRefreshSucceeded)
        => new()
        {
            Success = true,
            Value = value,
            RetryCount = retryCount,
            TokenRefreshAttempted = tokenRefreshAttempted,
            TokenRefreshSucceeded = tokenRefreshSucceeded
        };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static RetryResult<T> Failed(Exception exception, int retryCount, bool tokenRefreshAttempted, bool tokenRefreshSucceeded)
        => new()
        {
            Success = false,
            Exception = exception,
            RetryCount = retryCount,
            TokenRefreshAttempted = tokenRefreshAttempted,
            TokenRefreshSucceeded = tokenRefreshSucceeded
        };
}

/// <summary>
/// Helper class for handling retry logic with configurable max retries.
/// Designed to handle 401 Unauthorized errors with automatic token refresh.
/// </summary>
public class RetryHandler
{
    private readonly RetryOptions _options;

    /// <summary>
    /// Creates a new RetryHandler with the specified options.
    /// </summary>
    /// <param name="options">Retry configuration options</param>
    public RetryHandler(RetryOptions? options = null)
    {
        _options = options ?? new RetryOptions();
    }

    /// <summary>
    /// Gets the maximum number of auth retries allowed.
    /// </summary>
    public int MaxAuthRetries => _options.MaxAuthRetries;

    /// <summary>
    /// Gets whether auto token refresh is enabled.
    /// </summary>
    public bool AutoRefreshToken => _options.AutoRefreshToken;

    /// <summary>
    /// Executes an async operation with retry logic for 401 errors.
    /// </summary>
    /// <typeparam name="T">The type of the result</typeparam>
    /// <param name="operation">The async operation to execute</param>
    /// <param name="refreshToken">The async function to refresh the token</param>
    /// <param name="is401Error">Function to check if an exception is a 401 error</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>RetryResult containing the outcome</returns>
    public async Task<RetryResult<T>> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        Func<Task<bool>> refreshToken,
        Func<Exception, bool> is401Error,
        CancellationToken cancellationToken = default)
    {
        var retryCount = 0;
        var tokenRefreshAttempted = false;
        var tokenRefreshSucceeded = false;
        Exception? lastException = null;

        while (retryCount <= _options.MaxAuthRetries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await operation();
                return RetryResult<T>.Succeeded(result, retryCount, tokenRefreshAttempted, tokenRefreshSucceeded);
            }
            catch (Exception ex) when (is401Error(ex) && retryCount < _options.MaxAuthRetries)
            {
                lastException = ex;

                // Only attempt token refresh if enabled
                if (_options.AutoRefreshToken)
                {
                    tokenRefreshAttempted = true;

                    try
                    {
                        tokenRefreshSucceeded = await refreshToken();

                        if (!tokenRefreshSucceeded)
                        {
                            // Token refresh failed, return error immediately
                            // Requirement 2.4: WHEN refresh token thất bại, SHALL trả lỗi authentication về client ngay lập tức
                            return RetryResult<T>.Failed(ex, retryCount, tokenRefreshAttempted, tokenRefreshSucceeded);
                        }
                    }
                    catch (Exception refreshEx)
                    {
                        // Token refresh threw an exception
                        return RetryResult<T>.Failed(refreshEx, retryCount, tokenRefreshAttempted, false);
                    }
                }

                retryCount++;
            }
            catch (Exception ex)
            {
                // Non-401 error or max retries reached
                return RetryResult<T>.Failed(ex, retryCount, tokenRefreshAttempted, tokenRefreshSucceeded);
            }
        }

        // Should not reach here, but handle gracefully
        return RetryResult<T>.Failed(
            lastException ?? new InvalidOperationException("Retry exhausted without result"),
            retryCount,
            tokenRefreshAttempted,
            tokenRefreshSucceeded);
    }

    /// <summary>
    /// Checks if an HTTP status code indicates an authentication error (401).
    /// </summary>
    /// <param name="statusCode">The HTTP status code</param>
    /// <returns>True if the status code is 401 Unauthorized</returns>
    public static bool IsAuthenticationError(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.Unauthorized;

    /// <summary>
    /// Checks if an HttpRequestException indicates a 401 error.
    /// </summary>
    /// <param name="exception">The exception to check</param>
    /// <returns>True if the exception indicates a 401 error</returns>
    public static bool Is401Exception(Exception exception)
    {
        if (exception is HttpRequestException httpEx)
        {
            // Check StatusCode property if available (.NET 5+)
            if (httpEx.StatusCode == HttpStatusCode.Unauthorized)
                return true;

            // Fallback: check message for 401
            if (httpEx.Message.Contains("401") || httpEx.Message.Contains("Unauthorized"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Determines if a retry should be attempted based on current state.
    /// </summary>
    /// <param name="currentRetryCount">Current number of retries attempted</param>
    /// <returns>True if another retry is allowed</returns>
    public bool ShouldRetry(int currentRetryCount)
        => currentRetryCount < _options.MaxAuthRetries;
}
