namespace SemanticApiGateway.Gateway.Models;

/// <summary>
/// Standardized API error response for all HTTP endpoints.
/// Follows RFC 7807 Problem Details specification for consistent error handling.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// HTTP status code for this error response.
    /// </summary>
    public int StatusCode { get; set; } = 500;

    /// <summary>
    /// Short, human-readable error title.
    /// Examples: "Invalid Request", "Service Unavailable", "Unauthorized"
    /// </summary>
    public string Error { get; set; } = "Internal Server Error";

    /// <summary>
    /// Detailed error message providing context and recovery guidance.
    /// Should be clear enough for client developers to understand the issue.
    /// </summary>
    public string Details { get; set; } = string.Empty;

    /// <summary>
    /// Optional error code for programmatic error handling.
    /// Used to distinguish different error types with same HTTP status.
    /// Examples: "SERVICE_TIMEOUT", "AUTH_TOKEN_EXPIRED", "INVALID_INPUT"
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Optional timestamp when the error occurred (ISO 8601 format).
    /// </summary>
    public DateTime? Timestamp { get; set; }

    /// <summary>
    /// Optional trace ID for distributed tracing and debugging.
    /// Clients can use this to correlate with server logs.
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>
    /// Optional correlation ID for tracking request across multiple services.
    /// Useful in microservice architectures for end-to-end tracing.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Request path that triggered the error (for debugging).
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Additional error context (only in Development environment).
    /// Can contain suggestions, validation errors, or field-specific details.
    /// </summary>
    public Dictionary<string, object>? Context { get; set; }
}

/// <summary>
/// Enumeration of standardized error codes for programmatic handling.
/// </summary>
public static class ErrorCodes
{
    // Validation & Input Errors (4xx)
    public const string InvalidInput = "INVALID_INPUT";
    public const string MissingParameter = "MISSING_PARAMETER";
    public const string MalformedJson = "MALFORMED_JSON";
    public const string ValidationFailed = "VALIDATION_FAILED";

    // Authentication & Authorization (4xx)
    public const string Unauthorized = "UNAUTHORIZED";
    public const string TokenExpired = "TOKEN_EXPIRED";
    public const string TokenInvalid = "TOKEN_INVALID";
    public const string Forbidden = "FORBIDDEN";
    public const string InsufficientPermissions = "INSUFFICIENT_PERMISSIONS";

    // Resource Errors (4xx)
    public const string NotFound = "NOT_FOUND";
    public const string AlreadyExists = "ALREADY_EXISTS";
    public const string Conflict = "CONFLICT";
    public const string Gone = "GONE";

    // Service Errors (5xx)
    public const string InternalError = "INTERNAL_ERROR";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
    public const string ServiceTimeout = "SERVICE_TIMEOUT";
    public const string BadGateway = "BAD_GATEWAY";
    public const string GatewayTimeout = "GATEWAY_TIMEOUT";

    // Limit Exceeded (4xx)
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
    public const string QuotaExceeded = "QUOTA_EXCEEDED";
    public const string TooLarge = "PAYLOAD_TOO_LARGE";

    // Processing Errors (5xx)
    public const string ProcessingFailed = "PROCESSING_FAILED";
    public const string DownstreamError = "DOWNSTREAM_ERROR";
    public const string CircuitBreakerOpen = "CIRCUIT_BREAKER_OPEN";
    public const string RequestTimeout = "REQUEST_TIMEOUT";

    // Business Logic Errors (4xx-5xx)
    public const string OperationFailed = "OPERATION_FAILED";
    public const string InvalidState = "INVALID_STATE";
    public const string DependencyError = "DEPENDENCY_ERROR";
}
