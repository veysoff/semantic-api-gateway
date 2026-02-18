using System.Diagnostics;

namespace SemanticApiGateway.Gateway.Features.Observability;

/// <summary>
/// Custom ActivitySource for OpenTelemetry tracing of Gateway operations
/// Creates spans for intent execution, plugin loading, and function calls
/// </summary>
public class GatewayActivitySource : IGatewayActivitySource
{
    private readonly ActivitySource _activitySource;
    private readonly ILogger<GatewayActivitySource> _logger;

    public const string SourceName = "SemanticApiGateway";
    public const string SourceVersion = "1.0.0";

    public GatewayActivitySource(ILogger<GatewayActivitySource> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _activitySource = new ActivitySource(SourceName, SourceVersion);
    }

    /// <summary>
    /// Create a span for intent execution
    /// </summary>
    public Activity? StartIntentExecutionSpan(string intent, string userId)
    {
        try
        {
            var activity = _activitySource.StartActivity("ExecuteIntent");
            if (activity != null)
            {
                activity.SetTag("intent", intent);
                activity.SetTag("user_id", userId);
                activity.SetTag("timestamp", DateTime.UtcNow);
            }
            return activity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create intent execution span");
            return null;
        }
    }

    /// <summary>
    /// Create a span for plugin loading
    /// </summary>
    public Activity? StartPluginLoadingSpan(string serviceName, string swaggerUrl)
    {
        try
        {
            var activity = _activitySource.StartActivity("LoadPlugin");
            if (activity != null)
            {
                activity.SetTag("service", serviceName);
                activity.SetTag("swagger_url", swaggerUrl);
            }
            return activity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create plugin loading span");
            return null;
        }
    }

    /// <summary>
    /// Create a span for function invocation
    /// </summary>
    public Activity? StartFunctionInvocationSpan(string serviceName, string functionName, Dictionary<string, object> parameters)
    {
        try
        {
            var activity = _activitySource.StartActivity($"Invoke{functionName}");
            if (activity != null)
            {
                activity.SetTag("service", serviceName);
                activity.SetTag("function", functionName);
                activity.SetTag("parameter_count", parameters.Count);

                // Add parameter tags (be careful with sensitive data)
                foreach (var kvp in parameters)
                {
                    if (!IsSensitiveParameter(kvp.Key))
                    {
                        activity.SetTag($"param_{kvp.Key}", kvp.Value?.ToString() ?? "null");
                    }
                }
            }
            return activity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create function invocation span");
            return null;
        }
    }

    /// <summary>
    /// Create a span for security validation
    /// </summary>
    public Activity? StartSecurityValidationSpan(string userId, string validationType)
    {
        try
        {
            var activity = _activitySource.StartActivity("ValidateSecurity");
            if (activity != null)
            {
                activity.SetTag("user_id", userId);
                activity.SetTag("validation_type", validationType);
            }
            return activity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create security validation span");
            return null;
        }
    }

    /// <summary>
    /// Create a child span for step execution within an intent
    /// </summary>
    public Activity? StartStepExecutionSpan(int stepOrder, string serviceName, string functionName)
    {
        try
        {
            var activity = _activitySource.StartActivity($"ExecuteStep_{stepOrder}");
            if (activity != null)
            {
                activity.SetTag("step.order", stepOrder);
                activity.SetTag("step.service", serviceName);
                activity.SetTag("step.function", functionName);
                activity.SetTag("step.timestamp", DateTime.UtcNow);
            }
            return activity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create step execution span");
            return null;
        }
    }

    /// <summary>
    /// Record step execution metrics and results
    /// </summary>
    public void RecordStepMetrics(Activity? activity, bool success, long durationMs, int retryCount = 0, string? errorMessage = null)
    {
        if (activity == null)
            return;

        try
        {
            activity.SetTag("step.success", success);
            activity.SetTag("step.duration_ms", durationMs);
            activity.SetTag("step.retry_count", retryCount);

            if (!success && !string.IsNullOrWhiteSpace(errorMessage))
            {
                activity.SetTag("step.error", errorMessage);
                activity.SetStatus(ActivityStatusCode.Error, errorMessage);
            }
            else if (success)
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record step metrics");
        }
    }

    /// <summary>
    /// Create a child span for variable resolution
    /// </summary>
    public Activity? StartVariableResolutionSpan(string variablePattern)
    {
        try
        {
            var activity = _activitySource.StartActivity("ResolveVariable");
            if (activity != null)
            {
                activity.SetTag("variable.pattern", variablePattern);
                activity.SetTag("variable.timestamp", DateTime.UtcNow);
            }
            return activity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create variable resolution span");
            return null;
        }
    }

    /// <summary>
    /// Record variable resolution metrics
    /// </summary>
    public void RecordVariableMetrics(Activity? activity, bool success, long durationMs, string? resolvedValue = null, string? errorMessage = null)
    {
        if (activity == null)
            return;

        try
        {
            activity.SetTag("variable.success", success);
            activity.SetTag("variable.duration_ms", durationMs);

            if (success && !string.IsNullOrWhiteSpace(resolvedValue))
            {
                activity.SetTag("variable.resolved", "true");
            }

            if (!success && !string.IsNullOrWhiteSpace(errorMessage))
            {
                activity.SetTag("variable.error", errorMessage);
                activity.SetStatus(ActivityStatusCode.Error, errorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record variable metrics");
        }
    }

    /// <summary>
    /// Check if a parameter name indicates sensitive data
    /// </summary>
    private bool IsSensitiveParameter(string paramName)
    {
        var sensitiveKeywords = new[] { "password", "token", "secret", "api_key", "credential", "auth" };
        var lowerName = paramName.ToLowerInvariant();
        return sensitiveKeywords.Any(keyword => lowerName.Contains(keyword));
    }
}

/// <summary>
/// Interface for activity source operations
/// </summary>
public interface IGatewayActivitySource
{
    Activity? StartIntentExecutionSpan(string intent, string userId);
    Activity? StartPluginLoadingSpan(string serviceName, string swaggerUrl);
    Activity? StartFunctionInvocationSpan(string serviceName, string functionName, Dictionary<string, object> parameters);
    Activity? StartSecurityValidationSpan(string userId, string validationType);
    Activity? StartStepExecutionSpan(int stepOrder, string serviceName, string functionName);
    void RecordStepMetrics(Activity? activity, bool success, long durationMs, int retryCount = 0, string? errorMessage = null);
    Activity? StartVariableResolutionSpan(string variablePattern);
    void RecordVariableMetrics(Activity? activity, bool success, long durationMs, string? resolvedValue = null, string? errorMessage = null);
}
