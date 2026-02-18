using System.Collections.Concurrent;

namespace SemanticApiGateway.Gateway.Features.AuditTrail;

/// <summary>
/// In-memory implementation of audit service.
/// Suitable for development and small deployments. Use external service for production.
/// </summary>
public class InMemoryAuditService(ILogger<InMemoryAuditService> logger) : IAuditService
{
    private readonly ConcurrentBag<AuditLog> _logs = [];

    public async Task LogActionAsync(
        string userId,
        string action,
        string resource,
        string method,
        int statusCode,
        CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            UserId = userId ?? "unknown",
            Action = action,
            Resource = resource,
            Method = method,
            StatusCode = statusCode,
            Success = statusCode >= 200 && statusCode < 300,
            Timestamp = DateTime.UtcNow
        };

        _logs.Add(log);
        logger.LogInformation(
            "Audit: User {UserId} performed {Action} on {Resource} (status: {StatusCode})",
            userId,
            action,
            resource,
            statusCode);

        await Task.CompletedTask;
    }

    public async Task LogErrorAsync(
        string userId,
        string action,
        string resource,
        string method,
        int statusCode,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            UserId = userId ?? "unknown",
            Action = action,
            Resource = resource,
            Method = method,
            StatusCode = statusCode,
            Success = false,
            ErrorMessage = errorMessage,
            Timestamp = DateTime.UtcNow
        };

        _logs.Add(log);
        logger.LogWarning(
            "Audit Error: User {UserId} tried {Action} on {Resource} - {ErrorMessage}",
            userId,
            action,
            resource,
            errorMessage);

        await Task.CompletedTask;
    }

    public async Task<IEnumerable<AuditLog>> GetUserAuditLogsAsync(
        string userId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var logs = _logs
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .ToList();

        return await Task.FromResult(logs);
    }

    public async Task<IEnumerable<AuditLog>> GetResourceAuditLogsAsync(
        string resource,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var logs = _logs
            .Where(l => l.Resource == resource)
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .ToList();

        return await Task.FromResult(logs);
    }
}
