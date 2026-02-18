using System.Collections.Concurrent;

namespace SemanticApiGateway.Gateway.Features.RateLimiting;

/// <summary>
/// In-memory rate limiting service using token bucket algorithm.
/// Thread-safe and suitable for single-instance deployments.
/// For distributed systems, use RedisRateLimitingService.
/// </summary>
public class RateLimitingService(ILogger<RateLimitingService> logger) : IRateLimitingService
{
    private const int DefaultDailyLimit = 1000;
    private readonly ConcurrentDictionary<string, UserQuota> _quotas = new();

    public async Task<RateLimitResult> CheckLimitAsync(
        string userId,
        string resource = "intent",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return new RateLimitResult
            {
                Allowed = false,
                Reason = "User ID is required"
            };
        }

        var quota = _quotas.GetOrAdd(userId, _ => new UserQuota(DefaultDailyLimit));

        lock (quota)
        {
            if (quota.IsExpired())
            {
                quota.Reset();
            }

            var result = new RateLimitResult
            {
                Usage = new QuotaUsage
                {
                    DailyLimit = quota.DailyLimit,
                    RequestsUsedToday = quota.RequestsUsed,
                    RequestsRemaining = Math.Max(0, quota.DailyLimit - quota.RequestsUsed),
                    ResetTime = quota.ResetTime
                }
            };

            if (quota.RequestsUsed >= quota.DailyLimit)
            {
                result.Allowed = false;
                result.Reason = "Daily quota exceeded";
                var secondsUntilReset = (int)(quota.ResetTime - DateTime.UtcNow).TotalSeconds;
                result.RetryAfterSeconds = Math.Max(1, secondsUntilReset);

                logger.LogWarning(
                    "Rate limit exceeded for user {UserId}. Reset in {SecondsUntilReset}s",
                    userId,
                    secondsUntilReset);
            }
            else
            {
                quota.RequestsUsed++;
                result.Allowed = true;
                result.Usage.RequestsRemaining = Math.Max(0, quota.DailyLimit - quota.RequestsUsed);
            }

            return result;
        }
    }

    public async Task<QuotaUsage> GetUsageAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!_quotas.TryGetValue(userId, out var quota))
        {
            return new QuotaUsage
            {
                DailyLimit = DefaultDailyLimit,
                RequestsUsedToday = 0,
                RequestsRemaining = DefaultDailyLimit,
                ResetTime = DateTime.UtcNow.AddDays(1)
            };
        }

        lock (quota)
        {
            if (quota.IsExpired())
            {
                quota.Reset();
            }

            return new QuotaUsage
            {
                DailyLimit = quota.DailyLimit,
                RequestsUsedToday = quota.RequestsUsed,
                RequestsRemaining = Math.Max(0, quota.DailyLimit - quota.RequestsUsed),
                ResetTime = quota.ResetTime
            };
        }
    }

    public async Task ResetQuotaAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (_quotas.TryGetValue(userId, out var quota))
        {
            lock (quota)
            {
                quota.Reset();
            }

            logger.LogInformation("Quota reset for user {UserId}", userId);
        }
    }
}

/// <summary>
/// Internal user quota state (thread-safe with lock).
/// </summary>
internal class UserQuota
{
    public int DailyLimit { get; set; }
    public int RequestsUsed { get; set; }
    public DateTime ResetTime { get; set; }

    public UserQuota(int dailyLimit)
    {
        DailyLimit = dailyLimit;
        RequestsUsed = 0;
        ResetTime = DateTime.UtcNow.AddDays(1);
    }

    public bool IsExpired() => DateTime.UtcNow >= ResetTime;

    public void Reset()
    {
        RequestsUsed = 0;
        ResetTime = DateTime.UtcNow.AddDays(1);
    }
}
