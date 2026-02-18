using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using SemanticApiGateway.Gateway.Features.RateLimiting;

namespace SemanticApiGateway.Tests.ReasoningTests;

public class RedisRateLimitingServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _mockRedis;
    private readonly Mock<IDatabase> _mockDb;
    private readonly Mock<IRateLimitingService> _mockFallback;
    private readonly Mock<ILogger<RedisRateLimitingService>> _mockLogger;
    private readonly RedisRateLimitingService _service;

    public RedisRateLimitingServiceTests()
    {
        _mockRedis = new Mock<IConnectionMultiplexer>();
        _mockDb = new Mock<IDatabase>();
        _mockFallback = new Mock<IRateLimitingService>();
        _mockLogger = new Mock<ILogger<RedisRateLimitingService>>();

        _mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_mockDb.Object);

        _service = new RedisRateLimitingService(_mockRedis.Object, _mockFallback.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CheckLimitAsync_FirstRequest_AllowsAndIncrementsCounter()
    {
        var userId = "user-1";
        var resetTime = DateTime.UtcNow.AddDays(1).ToBinary();

        _mockDb.Setup(d => d.StringGetAsync("ratelimit:reset:user-1", It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(resetTime.ToString()));
        _mockDb.Setup(d => d.StringGetAsync("ratelimit:user-1", It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("0"));
        _mockDb.Setup(d => d.StringIncrementAsync("ratelimit:user-1", 1, CommandFlags.None))
            .ReturnsAsync(1);

        var result = await _service.CheckLimitAsync(userId);

        Assert.True(result.Allowed);
        Assert.Equal(999, result.Usage.RequestsRemaining);
        _mockDb.Verify(d => d.StringIncrementAsync("ratelimit:user-1", 1, CommandFlags.None), Times.Once);
    }

    [Fact]
    public async Task CheckLimitAsync_NullUserId_ReturnsFalse()
    {
        var result = await _service.CheckLimitAsync(null!);

        Assert.False(result.Allowed);
        Assert.Equal("User ID is required", result.Reason);
    }

    [Fact]
    public async Task CheckLimitAsync_QuotaExceeded_ReturnsFalseWithRetryAfter()
    {
        var userId = "user-2";
        var resetTime = DateTime.UtcNow.AddHours(1).ToBinary();

        _mockDb.Setup(d => d.StringGetAsync("ratelimit:reset:user-2", It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(resetTime.ToString()));
        _mockDb.Setup(d => d.StringGetAsync("ratelimit:user-2", It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("1000"));

        var result = await _service.CheckLimitAsync(userId);

        Assert.False(result.Allowed);
        Assert.Equal("Daily quota exceeded", result.Reason);
        Assert.NotNull(result.RetryAfterSeconds);
        Assert.True(result.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task CheckLimitAsync_RedisException_FallsBackToInMemory()
    {
        var userId = "user-3";
        var fallbackResult = new RateLimitResult { Allowed = true, Usage = new() { RequestsRemaining = 999 } };

        _mockDb.Setup(d => d.StringGetAsync("ratelimit:reset:user-3", CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));
        _mockFallback.Setup(f => f.CheckLimitAsync(userId, "intent", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fallbackResult);

        var result = await _service.CheckLimitAsync(userId);

        Assert.True(result.Allowed);
        _mockFallback.Verify(f => f.CheckLimitAsync(userId, "intent", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsQuotaFromRedis()
    {
        var userId = "user-4";
        var resetTime = DateTime.UtcNow.AddDays(1).ToBinary();

        _mockDb.Setup(d => d.StringGetAsync("ratelimit:user-4", It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue("250"));
        _mockDb.Setup(d => d.StringGetAsync("ratelimit:reset:user-4", It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(resetTime.ToString()));

        var usage = await _service.GetUsageAsync(userId);

        Assert.Equal(1000, usage.DailyLimit);
        Assert.Equal(250, usage.RequestsUsedToday);
        Assert.Equal(750, usage.RequestsRemaining);
    }

    [Fact]
    public async Task GetUsageAsync_RedisException_FallsBackToInMemory()
    {
        var userId = "user-5";
        var fallbackUsage = new QuotaUsage { DailyLimit = 1000, RequestsRemaining = 500 };

        _mockDb.Setup(d => d.StringGetAsync("ratelimit:user-5", CommandFlags.None))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection failed"));
        _mockFallback.Setup(f => f.GetUsageAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fallbackUsage);

        var usage = await _service.GetUsageAsync(userId);

        Assert.Equal(1000, usage.DailyLimit);
        _mockFallback.Verify(f => f.GetUsageAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

}
