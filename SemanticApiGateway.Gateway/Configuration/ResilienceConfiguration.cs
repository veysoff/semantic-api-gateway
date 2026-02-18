namespace SemanticApiGateway.Gateway.Configuration;

/// <summary>
/// Global resilience configuration for timeouts, retries, and service-specific overrides.
/// Loaded from appsettings.json "Resilience" section.
/// </summary>
public class ResilienceConfiguration
{
    /// <summary>
    /// Default timeout in seconds for step execution.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Default maximum retry attempts for failed steps.
    /// </summary>
    public int DefaultMaxRetries { get; set; } = 3;

    /// <summary>
    /// Default base backoff delay in milliseconds for exponential backoff strategy.
    /// </summary>
    public int DefaultBackoffMs { get; set; } = 100;

    /// <summary>
    /// Per-service timeout overrides in seconds.
    /// If a service key is present, its timeout takes precedence over DefaultTimeoutSeconds.
    /// </summary>
    public Dictionary<string, int> ServiceTimeouts { get; set; } = new();

    /// <summary>
    /// Per-service retry configuration overrides.
    /// If a service key is present, its settings override defaults.
    /// </summary>
    public Dictionary<string, ServiceResilienceConfig> ServiceRetries { get; set; } = new();

    /// <summary>
    /// Get the timeout in seconds for a specific service.
    /// </summary>
    /// <param name="serviceName">Name of the service (e.g., "OrderService")</param>
    /// <returns>Service-specific timeout, or default if not configured</returns>
    public int GetTimeoutSeconds(string serviceName)
    {
        return ServiceTimeouts.TryGetValue(serviceName, out var timeout)
            ? timeout
            : DefaultTimeoutSeconds;
    }

    /// <summary>
    /// Get the timeout in milliseconds for a specific service.
    /// </summary>
    /// <param name="serviceName">Name of the service (e.g., "OrderService")</param>
    /// <returns>Service-specific timeout in milliseconds, or default if not configured</returns>
    public int GetTimeoutMs(string serviceName)
    {
        return GetTimeoutSeconds(serviceName) * 1000;
    }

    /// <summary>
    /// Get the resilience configuration for a specific service.
    /// </summary>
    /// <param name="serviceName">Name of the service (e.g., "OrderService")</param>
    /// <returns>Service-specific configuration, or a new instance using defaults if not configured</returns>
    public ServiceResilienceConfig GetServiceConfig(string serviceName)
    {
        return ServiceRetries.TryGetValue(serviceName, out var config)
            ? config
            : new ServiceResilienceConfig
            {
                MaxRetries = DefaultMaxRetries,
                BackoffMs = DefaultBackoffMs
            };
    }
}
