namespace SemanticApiGateway.Gateway.Features.AuditTrail;

/// <summary>
/// Log entry for audit trail.
/// Records user actions for compliance and security analysis.
/// </summary>
public class AuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public Dictionary<string, object> Context { get; set; } = [];
}

/// <summary>
/// Service for recording audit logs.
/// Implementations can write to database, file, or external audit system.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Records a successful action to the audit trail.
    /// </summary>
    Task LogActionAsync(
        string userId,
        string action,
        string resource,
        string method,
        int statusCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed action with error details.
    /// </summary>
    Task LogErrorAsync(
        string userId,
        string action,
        string resource,
        string method,
        int statusCode,
        string errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves audit logs for a specific user.
    /// </summary>
    Task<IEnumerable<AuditLog>> GetUserAuditLogsAsync(
        string userId,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves audit logs for a specific resource.
    /// </summary>
    Task<IEnumerable<AuditLog>> GetResourceAuditLogsAsync(
        string resource,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
