namespace SemanticApiGateway.Gateway.Models;

public class ExecuteIntentRequest
{
    public string Intent { get; set; } = string.Empty;
    public Dictionary<string, object>? Context { get; set; }
}

public class ExecuteIntentResponse
{
    public bool Success { get; set; }
    public object? Result { get; set; }
    public long ExecutionTimeMs { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string PlanId { get; set; } = string.Empty;
}

public class PlanIntentResponse
{
    public string PlanId { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public List<StepDto> Steps { get; set; } = new();
}

public class StepDto
{
    public int Order { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}
