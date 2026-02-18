namespace SemanticApiGateway.Gateway.Models;

/// <summary>
/// Comprehensive error report for step execution failures.
/// Aggregates errors from all retry attempts and provides recovery guidance.
/// </summary>
public class ExecutionErrorReport
{
    /// <summary>
    /// Name of the step that failed.
    /// </summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>
    /// Service name that the step called (e.g., "OrderService").
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Function name that was called on the service.
    /// </summary>
    public string? FunctionName { get; set; }

    /// <summary>
    /// All errors encountered during step execution.
    /// Usually just one, but can contain multiple if retries were involved.
    /// </summary>
    public List<StepError> Errors { get; set; } = new();

    /// <summary>
    /// Total number of retry attempts made.
    /// </summary>
    public int TotalRetryAttempts { get; set; }

    /// <summary>
    /// Total time spent on step execution including retries.
    /// </summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>
    /// Whether the error is recoverable (transient) or permanent.
    /// </summary>
    public bool IsRecoverable { get; set; }

    /// <summary>
    /// Recommendation for how to handle this error.
    /// </summary>
    public string? RecoveryRecommendation { get; set; }

    /// <summary>
    /// Timestamp when the error occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
