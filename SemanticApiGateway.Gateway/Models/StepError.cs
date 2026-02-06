namespace SemanticApiGateway.Gateway.Models;

/// <summary>
/// Structured error information for step execution failures.
/// Provides rich context for debugging and recovery decisions.
/// </summary>
public class StepError
{
    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error category (Transient, Permanent, or Unknown).
    /// Used to determine if retry is appropriate.
    /// </summary>
    public ErrorCategory Category { get; set; } = ErrorCategory.Unknown;

    /// <summary>
    /// Number of retry attempts made.
    /// </summary>
    public int RetryAttempts { get; set; }

    /// <summary>
    /// Total duration spent retrying (excluding initial attempt).
    /// </summary>
    public TimeSpan TotalRetryDuration { get; set; }

    /// <summary>
    /// Detailed history of each retry attempt with timing and error info.
    /// </summary>
    public List<RetryAttempt> RetryHistory { get; set; } = new();

    /// <summary>
    /// Stack trace for debugging (included in Development mode).
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// HTTP status code if the error came from an HTTP response (e.g., 503, 504).
    /// </summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>
    /// Whether this error was recovered via fallback value.
    /// </summary>
    public bool UsedFallback { get; set; }

    /// <summary>
    /// The fallback value that was used, if any.
    /// </summary>
    public object? FallbackValue { get; set; }
}
