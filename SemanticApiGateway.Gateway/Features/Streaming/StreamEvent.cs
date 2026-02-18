namespace SemanticApiGateway.Gateway.Features.Streaming;

/// <summary>
/// Represents a single streaming event during intent execution.
/// Events are sent to clients via Server-Sent Events (SSE).
/// </summary>
public record StreamEvent
{
    /// <summary>
    /// Event type: step_started, step_progress, step_completed, step_failed, execution_completed
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// Step order in execution plan (0 for plan/execution level events)
    /// </summary>
    public int StepOrder { get; init; }

    /// <summary>
    /// Microservice name (e.g., "UserService", "OrderService")
    /// </summary>
    public string? ServiceName { get; init; }

    /// <summary>
    /// Function name being executed (e.g., "GetUser", "CreateOrder")
    /// </summary>
    public string? FunctionName { get; init; }

    /// <summary>
    /// Event-specific data payload (result, error details, progress info)
    /// </summary>
    public object? Data { get; init; }

    /// <summary>
    /// UTC timestamp when event was created
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Duration in milliseconds for completed operations
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Unique correlation ID for tracing related events
    /// </summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Stream event types for intent execution
/// </summary>
public static class StreamEventTypes
{
    public const string ExecutionStarted = "execution_started";
    public const string PlanGenerated = "plan_generated";
    public const string StepStarted = "step_started";
    public const string StepProgress = "step_progress";
    public const string StepCompleted = "step_completed";
    public const string StepFailed = "step_failed";
    public const string ExecutionCompleted = "execution_completed";
    public const string ExecutionFailed = "execution_failed";
}
