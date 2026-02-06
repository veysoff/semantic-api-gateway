using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using SemanticApiGateway.Gateway.Features.AuditTrail;

namespace SemanticApiGateway.Tests.ReasoningTests;

public class InMemoryAuditServiceTests
{
    private readonly Mock<ILogger<InMemoryAuditService>> _mockLogger;
    private readonly InMemoryAuditService _service;

    public InMemoryAuditServiceTests()
    {
        _mockLogger = new Mock<ILogger<InMemoryAuditService>>();
        _service = new InMemoryAuditService(_mockLogger.Object);
    }

    [Fact]
    public async Task LogActionAsync_SuccessfulRequest_RecordsLog()
    {
        var userId = "user-1";
        var action = "create";
        var resource = "/api/orders";
        var method = "POST";

        await _service.LogActionAsync(userId, action, resource, method, 201);
        var logs = await _service.GetUserAuditLogsAsync(userId);

        Assert.Single(logs);
        var log = logs.First();
        Assert.Equal(userId, log.UserId);
        Assert.Equal(action, log.Action);
        Assert.Equal(resource, log.Resource);
        Assert.True(log.Success);
        Assert.Equal(201, log.StatusCode);
    }

    [Fact]
    public async Task LogErrorAsync_FailedRequest_RecordsErrorMessage()
    {
        var userId = "user-2";
        var action = "delete";
        var resource = "/api/orders/123";
        var method = "DELETE";
        var errorMessage = "Permission denied";

        await _service.LogErrorAsync(userId, action, resource, method, 403, errorMessage);
        var logs = await _service.GetUserAuditLogsAsync(userId);

        Assert.Single(logs);
        var log = logs.First();
        Assert.False(log.Success);
        Assert.Equal(403, log.StatusCode);
        Assert.Equal(errorMessage, log.ErrorMessage);
    }

    [Fact]
    public async Task LogActionAsync_NullUserId_DefaultsToUnknown()
    {
        await _service.LogActionAsync(null!, "read", "/api/users", "GET", 200);
        var logs = await _service.GetUserAuditLogsAsync("unknown");

        Assert.Single(logs);
        Assert.Equal("unknown", logs.First().UserId);
    }

    [Fact]
    public async Task GetUserAuditLogsAsync_MultipleActions_ReturnsMostRecent()
    {
        var userId = "user-3";

        await _service.LogActionAsync(userId, "read", "/api/data", "GET", 200);
        await Task.Delay(10);
        await _service.LogActionAsync(userId, "update", "/api/data", "PUT", 200);
        await Task.Delay(10);
        await _service.LogActionAsync(userId, "delete", "/api/data", "DELETE", 204);

        var logs = await _service.GetUserAuditLogsAsync(userId);

        Assert.Equal(3, logs.Count());
        Assert.Equal("delete", logs.First().Action);
        Assert.Equal("update", logs.Skip(1).First().Action);
        Assert.Equal("read", logs.Last().Action);
    }

    [Fact]
    public async Task GetResourceAuditLogsAsync_FiltersByResource()
    {
        var resource = "/api/orders";

        await _service.LogActionAsync("user-1", "create", resource, "POST", 201);
        await _service.LogActionAsync("user-2", "read", resource, "GET", 200);
        await _service.LogActionAsync("user-3", "create", "/api/users", "POST", 201);

        var logs = await _service.GetResourceAuditLogsAsync(resource);

        Assert.Equal(2, logs.Count());
        Assert.All(logs, log => Assert.Equal(resource, log.Resource));
    }

    [Fact]
    public async Task GetUserAuditLogsAsync_RespectLimit()
    {
        var userId = "user-4";

        for (int i = 0; i < 15; i++)
        {
            await _service.LogActionAsync(userId, "access", "/api/data", "GET", 200);
        }

        var logs = await _service.GetUserAuditLogsAsync(userId, limit: 5);

        Assert.Equal(5, logs.Count());
    }

    [Fact]
    public async Task LogActionAsync_StatusCode400_MarkedAsFailure()
    {
        await _service.LogActionAsync("user-5", "create", "/api/items", "POST", 400);
        var logs = await _service.GetUserAuditLogsAsync("user-5");

        var log = logs.First();
        Assert.False(log.Success);
    }

    [Fact]
    public async Task AuditLog_HasTimestamp()
    {
        var beforeTime = DateTime.UtcNow;
        await _service.LogActionAsync("user-6", "read", "/api/config", "GET", 200);
        var afterTime = DateTime.UtcNow;

        var logs = await _service.GetUserAuditLogsAsync("user-6");
        var log = logs.First();

        Assert.True(log.Timestamp >= beforeTime && log.Timestamp <= afterTime);
    }

    [Fact]
    public async Task AuditLog_GeneratesUniqueId()
    {
        await _service.LogActionAsync("user-7", "read", "/api/data", "GET", 200);
        await _service.LogActionAsync("user-7", "read", "/api/data", "GET", 200);

        var logs = await _service.GetUserAuditLogsAsync("user-7");
        var ids = logs.Select(l => l.Id).ToList();

        Assert.Equal(2, ids.Distinct().Count());
    }
}
