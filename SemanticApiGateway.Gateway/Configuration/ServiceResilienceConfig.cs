namespace SemanticApiGateway.Gateway.Configuration;

/// <summary>
/// Per-service resilience configuration for retry and timeout settings.
/// </summary>
public class ServiceResilienceConfig
{
    /// <summary>
    /// Maximum number of retry attempts for this service.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Base backoff delay in milliseconds for exponential backoff.
    /// Actual delay = BackoffMs * 2^(attemptNumber)
    /// </summary>
    public int BackoffMs { get; set; } = 100;
}
