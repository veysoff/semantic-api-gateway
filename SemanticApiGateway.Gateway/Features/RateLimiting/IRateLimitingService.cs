namespace SemanticApiGateway.Gateway.Features.RateLimiting;

/// <summary>
/// Service for enforcing rate limits and quotas on a per-user basis.
/// </summary>
public interface IRateLimitingService
{
    /// <summary>
    /// Checks if a request from the specified user should be allowed.
    /// Returns quota information regardless of allow/deny decision.
    /// </summary>
    Task<RateLimitResult> CheckLimitAsync(
        string userId,
        string resource = "intent",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current quota usage for a user without consuming the limit.
    /// </summary>
    Task<QuotaUsage> GetUsageAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets quota for a user (admin operation).
    /// </summary>
    Task ResetQuotaAsync(
        string userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of rate limit check.
/// </summary>
public class RateLimitResult
{
    public bool Allowed { get; set; }
    public QuotaUsage Usage { get; set; } = new();
    public int? RetryAfterSeconds { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// Current quota usage for a user.
/// </summary>
public class QuotaUsage
{
    public int RequestsRemaining { get; set; }
    public int DailyLimit { get; set; }
    public int RequestsUsedToday { get; set; }
    public DateTime ResetTime { get; set; }
    public double PercentageUsed => DailyLimit > 0 ? (RequestsUsedToday / (double)DailyLimit) * 100 : 0;
}
