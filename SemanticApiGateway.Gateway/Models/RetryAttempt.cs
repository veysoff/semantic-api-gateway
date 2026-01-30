namespace SemanticApiGateway.Gateway.Models;

/// <summary>
/// Records details of a single retry attempt for error analysis and debugging.
/// </summary>
public class RetryAttempt
{
    /// <summary>
    /// Which retry attempt this was (1-based, so 1 is the first retry after initial failure).
    /// </summary>
    public int AttemptNumber { get; set; }

    /// <summary>
    /// Timestamp when this retry occurred.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Error message from the failed attempt.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// How long the system waited before retrying (exponential backoff).
    /// </summary>
    public TimeSpan WaitBeforeRetry { get; set; }

    /// <summary>
    /// HTTP status code if applicable (e.g., 503 for Service Unavailable).
    /// </summary>
    public int? HttpStatusCode { get; set; }
}
