namespace SemanticApiGateway.Gateway.Features.Reasoning;

/// <summary>
/// Orchestrates multi-step API calls based on natural language intent
/// Plans execution steps and pipes data between microservices
/// </summary>
public interface IReasoningEngine
{
    /// <summary>
    /// Execute a natural language intent through orchestrated microservice calls
    /// </summary>
    /// <param name="intent">Natural language description of what the user wants</param>
    /// <param name="userId">ID of the user making the request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Aggregated result from the execution plan</returns>
    Task<ExecutionResult> ExecuteIntentAsync(string intent, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate an execution plan from natural language intent without executing it
    /// </summary>
    /// <param name="intent">Natural language description</param>
    /// <param name="userId">ID of the user</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The planned execution steps</returns>
    Task<ExecutionPlan> PlanIntentAsync(string intent, string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the execution plan generated from an intent
/// </summary>
public class ExecutionPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Intent { get; set; } = string.Empty;
    public List<ExecutionStep> Steps { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a single step in the execution plan
/// </summary>
public class ExecutionStep
{
    public int Order { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public string? DataSource { get; set; } // Reference to previous step output for data piping
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Represents the result of executing an intent
/// </summary>
public class ExecutionResult
{
    public string PlanId { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public bool Success { get; set; }
    public object? AggregatedResult { get; set; }
    public List<StepResult> StepResults { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents the result of a single execution step
/// </summary>
public class StepResult
{
    public int Order { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public object? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
}
