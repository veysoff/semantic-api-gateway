using SemanticApiGateway.Gateway.Features.RateLimiting;

namespace SemanticApiGateway.Gateway.Middleware;

/// <summary>
/// Middleware for enforcing rate limits on a per-user basis.
/// Applies limits to API requests before they reach handlers.
/// </summary>
public class RateLimitingMiddleware
{
    private const string RateLimitHeaderLimit = "X-RateLimit-Limit";
    private const string RateLimitHeaderRemaining = "X-RateLimit-Remaining";
    private const string RateLimitHeaderReset = "X-RateLimit-Reset";
    private const string RetryAfterHeader = "Retry-After";

    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly IRateLimitingService _rateLimitingService;

    public RateLimitingMiddleware(
        RequestDelegate next,
        ILogger<RateLimitingMiddleware> logger,
        IRateLimitingService rateLimitingService)
    {
        _next = next;
        _logger = logger;
        _rateLimitingService = rateLimitingService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = ExtractUserId(context);

        if (string.IsNullOrEmpty(userId))
        {
            await _next(context);
            return;
        }

        var result = await _rateLimitingService.CheckLimitAsync(userId);

        AddRateLimitHeaders(context, result);

        if (!result.Allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            if (result.RetryAfterSeconds.HasValue)
            {
                context.Response.Headers[RetryAfterHeader] = result.RetryAfterSeconds.ToString();
            }

            _logger.LogWarning(
                "Rate limit enforced for user {UserId}. Reason: {Reason}",
                userId,
                result.Reason);

            await context.Response.WriteAsJsonAsync(new
            {
                error = "Rate Limit Exceeded",
                message = result.Reason,
                retryAfter = result.RetryAfterSeconds
            });

            return;
        }

        await _next(context);
    }

    private static string? ExtractUserId(HttpContext context)
    {
        var userIdClaim = context.User?.FindFirst("sub")?.Value;
        return userIdClaim ?? context.User?.Identity?.Name;
    }

    private static void AddRateLimitHeaders(HttpContext context, RateLimitResult result)
    {
        context.Response.Headers[RateLimitHeaderLimit] = result.Usage.DailyLimit.ToString();
        context.Response.Headers[RateLimitHeaderRemaining] = result.Usage.RequestsRemaining.ToString();

        var resetUnixTime = new DateTimeOffset(result.Usage.ResetTime).ToUnixTimeSeconds();
        context.Response.Headers[RateLimitHeaderReset] = resetUnixTime.ToString();
    }
}

/// <summary>
/// Extension for registering rate limiting middleware.
/// </summary>
public static class RateLimitingExtensions
{
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RateLimitingMiddleware>();
    }
}
