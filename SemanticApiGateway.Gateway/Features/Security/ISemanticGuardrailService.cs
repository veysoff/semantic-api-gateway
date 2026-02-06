namespace SemanticApiGateway.Gateway.Features.Security;

/// <summary>
/// Validates semantic intent before execution
/// Detects prompt injection, enforces RBAC, and applies rate limiting
/// </summary>
public interface ISemanticGuardrailService
{
    /// <summary>
    /// Validate an intent for security threats
    /// </summary>
    /// <param name="intent">User intent text</param>
    /// <param name="userId">User ID making the request</param>
    /// <returns>Validation result with details</returns>
    Task<GuardrailValidationResult> ValidateIntentAsync(string intent, string userId);

    /// <summary>
    /// Check if user has exceeded rate limit
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>True if limit exceeded</returns>
    Task<bool> IsRateLimitedAsync(string userId);

    /// <summary>
    /// Record an intent execution for audit logging
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="intent">Intent text</param>
    /// <param name="success">Whether execution succeeded</param>
    /// <param name="result">Execution result</param>
    Task RecordIntentExecutionAsync(string userId, string intent, bool success, string? result);
}

/// <summary>
/// Result of guardrail validation
/// </summary>
public class GuardrailValidationResult
{
    public bool IsAllowed { get; set; }
    public string? ReasonDenied { get; set; }
    public List<string> TriggeredChecks { get; set; } = new();
    public GuardrailValidationType ValidationType { get; set; }
}

/// <summary>
/// Types of guardrail validation performed
/// </summary>
public enum GuardrailValidationType
{
    PromptInjectionDetected,
    RateLimitExceeded,
    UnauthorizedOperation,
    SensitiveOperationDetected,
    FunctionBlacklisted,
    Valid
}
