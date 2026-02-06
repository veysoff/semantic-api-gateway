using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using SemanticApiGateway.Gateway.Features.RateLimiting;

namespace SemanticApiGateway.Tests.ReasoningTests;

public class RateLimitingServiceTests
{
    private readonly Mock<ILogger<RateLimitingService>> _mockLogger;
    private readonly IRateLimitingService _service;

    public RateLimitingServiceTests()
    {
        _mockLogger = new Mock<ILogger<RateLimitingService>>();
        _service = new RateLimitingService(_mockLogger.Object);
    }

    [Fact]
    public async Task CheckLimit_FirstRequest_IsAllowed()
    {
        var result = await _service.CheckLimitAsync("test-user-1");
        Assert.True(result.Allowed);
        Assert.True(result.Usage.RequestsRemaining > 0);
    }

    [Fact]
    public async Task CheckLimit_MultipleRequests_DecrementsQuota()
    {
        var userId = "test-user-2";
        var first = await _service.CheckLimitAsync(userId);
        var second = await _service.CheckLimitAsync(userId);

        Assert.True(first.Allowed);
        Assert.True(second.Allowed);
        Assert.True(first.Usage.RequestsRemaining > second.Usage.RequestsRemaining);
    }

    [Fact]
    public async Task CheckLimit_ExceedsQuota_IsBlocked()
    {
        var userId = "test-user-3";

        for (int i = 0; i < 1000; i++)
        {
            await _service.CheckLimitAsync(userId);
        }

        var blocked = await _service.CheckLimitAsync(userId);
        Assert.False(blocked.Allowed);
        Assert.Contains("exceeded", blocked.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckLimit_NullUserId_IsRejected()
    {
        var result = await _service.CheckLimitAsync(null!);
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task GetUsage_ReturnsQuotaInfo()
    {
        var userId = "test-user-4";
        await _service.CheckLimitAsync(userId);

        var usage = await _service.GetUsageAsync(userId);

        Assert.True(usage.DailyLimit > 0);
        Assert.True(usage.RequestsRemaining > 0);
        Assert.Equal(usage.DailyLimit, usage.RequestsRemaining + usage.RequestsUsedToday);
    }

    [Fact]
    public async Task GetUsage_UnknownUser_ReturnsDefaults()
    {
        var usage = await _service.GetUsageAsync("unknown-user-999");

        Assert.Equal(1000, usage.DailyLimit);
        Assert.Equal(0, usage.RequestsUsedToday);
        Assert.Equal(1000, usage.RequestsRemaining);
    }

    [Fact]
    public async Task ResetQuota_ClearsUsedCount()
    {
        var userId = "test-user-5";

        for (int i = 0; i < 10; i++)
        {
            await _service.CheckLimitAsync(userId);
        }

        await _service.ResetQuotaAsync(userId);
        var usage = await _service.GetUsageAsync(userId);

        Assert.Equal(1000, usage.RequestsRemaining);
        Assert.Equal(0, usage.RequestsUsedToday);
    }

    [Fact]
    public async Task UsagePercentage_CalculatesCorrectly()
    {
        var userId = "test-user-6";

        for (int i = 0; i < 250; i++)
        {
            await _service.CheckLimitAsync(userId);
        }

        var usage = await _service.GetUsageAsync(userId);
        Assert.Equal(25.0, usage.PercentageUsed, 0.1);
    }
}
