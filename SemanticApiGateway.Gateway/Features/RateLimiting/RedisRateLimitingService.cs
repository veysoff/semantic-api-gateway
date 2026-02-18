using StackExchange.Redis;
using System.Collections.Concurrent;

namespace SemanticApiGateway.Gateway.Features.RateLimiting;

/// <summary>
/// Distributed rate limiting service using Redis.
/// Suitable for multi-instance deployments where quota must be shared across servers.
/// Falls back to in-memory service if Redis is unavailable.
/// </summary>
public class RedisRateLimitingService(
    IConnectionMultiplexer redis,
    IRateLimitingService fallbackService,
    ILogger<RedisRateLimitingService> logger) : IRateLimitingService
{
    private const int DefaultDailyLimit = 1000;
    private const string RedisKeyPrefix = "ratelimit:";
    private const string RedisResetKeyPrefix = "ratelimit:reset:";
    private readonly ConcurrentDictionary<string, bool> _redisHealthStatus = new();

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

        if (!IsRedisHealthy(userId))
        {
            logger.LogWarning("Redis unavailable for user {UserId}, using fallback service", userId);
            return await fallbackService.CheckLimitAsync(userId, resource, cancellationToken);
        }

        try
        {
            var db = redis.GetDatabase();
            var key = $"{RedisKeyPrefix}{userId}";
            var resetKey = $"{RedisResetKeyPrefix}{userId}";

            var resetTime = await db.StringGetAsync(resetKey);
            var shouldReset = !resetTime.HasValue ||
                            DateTime.UtcNow >= DateTime.FromBinary(long.Parse(resetTime.ToString() ?? "0"));

            if (shouldReset)
            {
                await ResetRedisQuotaAsync(db, userId, key, resetKey);
            }

            var requestsUsed = await db.StringGetAsync(key);
            var used = requestsUsed.HasValue ? int.Parse(requestsUsed.ToString()!) : 0;

            var result = new RateLimitResult
            {
                Usage = new QuotaUsage
                {
                    DailyLimit = DefaultDailyLimit,
                    RequestsUsedToday = used,
                    RequestsRemaining = Math.Max(0, DefaultDailyLimit - used),
                    ResetTime = ParseResetTime(resetTime)
                }
            };

            if (used >= DefaultDailyLimit)
            {
                result.Allowed = false;
                result.Reason = "Daily quota exceeded";
                var secondsUntilReset = (int)(result.Usage.ResetTime - DateTime.UtcNow).TotalSeconds;
                result.RetryAfterSeconds = Math.Max(1, secondsUntilReset);

                logger.LogWarning(
                    "Redis rate limit exceeded for user {UserId}. Reset in {SecondsUntilReset}s",
                    userId,
                    secondsUntilReset);
            }
            else
            {
                await db.StringIncrementAsync(key);
                result.Allowed = true;
                result.Usage.RequestsRemaining = Math.Max(0, DefaultDailyLimit - used - 1);
            }

            MarkRedisHealthy(userId);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Redis error for user {UserId}, using fallback service", userId);
            MarkRedisUnhealthy(userId);
            return await fallbackService.CheckLimitAsync(userId, resource, cancellationToken);
        }
    }

    public async Task<QuotaUsage> GetUsageAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!IsRedisHealthy(userId))
        {
            logger.LogWarning("Redis unavailable for user {UserId}, using fallback service", userId);
            return await fallbackService.GetUsageAsync(userId, cancellationToken);
        }

        try
        {
            var db = redis.GetDatabase();
            var key = $"{RedisKeyPrefix}{userId}";
            var resetKey = $"{RedisResetKeyPrefix}{userId}";

            var requestsUsed = await db.StringGetAsync(key);
            var resetTime = await db.StringGetAsync(resetKey);
            var used = requestsUsed.HasValue ? int.Parse(requestsUsed.ToString()!) : 0;

            MarkRedisHealthy(userId);
            return new QuotaUsage
            {
                DailyLimit = DefaultDailyLimit,
                RequestsUsedToday = used,
                RequestsRemaining = Math.Max(0, DefaultDailyLimit - used),
                ResetTime = ParseResetTime(resetTime)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Redis error for user {UserId}, using fallback service", userId);
            MarkRedisUnhealthy(userId);
            return await fallbackService.GetUsageAsync(userId, cancellationToken);
        }
    }

    public async Task ResetQuotaAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (!IsRedisHealthy(userId))
        {
            logger.LogWarning("Redis unavailable for user {UserId}, using fallback service", userId);
            await fallbackService.ResetQuotaAsync(userId, cancellationToken);
            return;
        }

        try
        {
            var db = redis.GetDatabase();
            var key = $"{RedisKeyPrefix}{userId}";
            var resetKey = $"{RedisResetKeyPrefix}{userId}";

            await ResetRedisQuotaAsync(db, userId, key, resetKey);
            MarkRedisHealthy(userId);
            logger.LogInformation("Quota reset for user {UserId} in Redis", userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Redis error resetting quota for user {UserId}, using fallback service", userId);
            MarkRedisUnhealthy(userId);
            await fallbackService.ResetQuotaAsync(userId, cancellationToken);
        }
    }

    private async Task ResetRedisQuotaAsync(IDatabase db, string userId, string key, string resetKey)
    {
        var resetTime = DateTime.UtcNow.AddDays(1).ToBinary();
        await db.StringSetAsync(key, 0);
        await db.StringSetAsync(resetKey, resetTime.ToString());
        await db.KeyExpireAsync(key, TimeSpan.FromDays(1));
        await db.KeyExpireAsync(resetKey, TimeSpan.FromDays(1));
    }

    private DateTime ParseResetTime(RedisValue resetValue)
    {
        if (!resetValue.HasValue)
        {
            return DateTime.UtcNow.AddDays(1);
        }

        if (long.TryParse(resetValue.ToString(), out var binary))
        {
            return DateTime.FromBinary(binary);
        }

        return DateTime.UtcNow.AddDays(1);
    }

    private bool IsRedisHealthy(string userId)
    {
        return !_redisHealthStatus.TryGetValue(userId, out var isUnhealthy) || !isUnhealthy;
    }

    private void MarkRedisHealthy(string userId)
    {
        _redisHealthStatus.TryUpdate(userId, false, true);
        _redisHealthStatus.TryRemove(userId, out _);
    }

    private void MarkRedisUnhealthy(string userId)
    {
        _redisHealthStatus.AddOrUpdate(userId, true, (_, _) => true);
    }
}
