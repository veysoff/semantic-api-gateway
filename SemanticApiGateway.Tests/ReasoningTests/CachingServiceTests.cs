using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using SemanticApiGateway.Gateway.Features.Caching;

namespace SemanticApiGateway.Tests.ReasoningTests;

internal class TestCacheModel
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}

internal class TestComplexCacheModel
{
    public string Name { get; set; } = string.Empty;
    public string[] Steps { get; set; } = [];
    public TestMetadata Metadata { get; set; } = new();
}

internal class TestMetadata
{
    public int Version { get; set; }
    public string UserId { get; set; } = string.Empty;
}

public class InMemoryCacheServiceTests
{
    private readonly Mock<ILogger<InMemoryCacheService>> _mockLogger;
    private readonly InMemoryCacheService _cache;

    public InMemoryCacheServiceTests()
    {
        _mockLogger = new Mock<ILogger<InMemoryCacheService>>();
        _cache = new InMemoryCacheService(_mockLogger.Object);
    }

    [Fact]
    public async Task SetAsync_GetAsync_ReturnsStoredValue()
    {
        var key = "test-key";
        var value = new TestCacheModel { Name = "Test", Value = 42 };

        await _cache.SetAsync(key, value);
        var result = await _cache.GetAsync<TestCacheModel>(key);

        Assert.NotNull(result);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsNull()
    {
        var result = await _cache.GetAsync<string>("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_WithTTL_ExpiresAfterTimeout()
    {
        var key = "expiring-key";
        var value = "test-value";
        var ttl = TimeSpan.FromMilliseconds(100);

        await _cache.SetAsync(key, value, ttl);
        var beforeExpire = await _cache.GetAsync<string>(key);
        Assert.NotNull(beforeExpire);

        await Task.Delay(150);
        var afterExpire = await _cache.GetAsync<string>(key);
        Assert.Null(afterExpire);
    }

    [Fact]
    public async Task SetAsync_WithoutTTL_PersistsIndefinitely()
    {
        var key = "persistent-key";
        var value = "persistent-value";

        await _cache.SetAsync(key, value);
        await Task.Delay(50);

        var result = await _cache.GetAsync<string>(key);
        Assert.Equal(value, result);
    }

    [Fact]
    public async Task RemoveAsync_DeletesKey()
    {
        var key = "removable-key";
        var value = "value";

        await _cache.SetAsync(key, value);
        await _cache.RemoveAsync(key);

        var result = await _cache.GetAsync<string>(key);
        Assert.Null(result);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllEntries()
    {
        await _cache.SetAsync("key1", "value1");
        await _cache.SetAsync("key2", "value2");
        await _cache.SetAsync("key3", "value3");

        await _cache.ClearAsync();

        var result1 = await _cache.GetAsync<string>("key1");
        var result2 = await _cache.GetAsync<string>("key2");
        var result3 = await _cache.GetAsync<string>("key3");

        Assert.Null(result1);
        Assert.Null(result2);
        Assert.Null(result3);
    }

    [Fact]
    public async Task GetStatsAsync_TracksCacheMetrics()
    {
        await _cache.SetAsync("key1", "value1");
        await _cache.SetAsync("key2", "value2");

        await _cache.GetAsync<string>("key1"); // Hit
        await _cache.GetAsync<string>("key1"); // Hit
        await _cache.GetAsync<string>("missing"); // Miss

        var stats = await _cache.GetStatsAsync();

        Assert.Equal(2, stats.TotalEntries);
        Assert.Equal(2, stats.Hits);
        Assert.Equal(1, stats.Misses);
        Assert.True(stats.HitRate > 0.5);
    }

    [Fact]
    public async Task SetAsync_UpdateExistingKey_ReplacesValue()
    {
        var key = "update-key";

        await _cache.SetAsync(key, "original");
        await _cache.SetAsync(key, "updated");

        var result = await _cache.GetAsync<string>(key);
        Assert.Equal("updated", result);
    }

    [Fact]
    public async Task GetStatsAsync_EmptyCache_ReturnsZeros()
    {
        var stats = await _cache.GetStatsAsync();

        Assert.Equal(0, stats.TotalEntries);
        Assert.Equal(0, stats.Hits);
        Assert.Equal(0, stats.Misses);
    }

    [Fact]
    public async Task SetAsync_ComplexObject_SerializesAndDeserializes()
    {
        var key = "complex-key";
        var value = new TestComplexCacheModel
        {
            Name = "TestIntent",
            Steps = new[] { "step1", "step2", "step3" },
            Metadata = new TestMetadata { Version = 1, UserId = "user-123" }
        };

        await _cache.SetAsync(key, value);
        var result = await _cache.GetAsync<TestComplexCacheModel>(key);

        Assert.NotNull(result);
        Assert.Equal("TestIntent", result.Name);
        Assert.Equal(3, result.Steps.Length);
        Assert.Equal("user-123", result.Metadata.UserId);
    }

    [Fact]
    public async Task Concurrent_SetGet_ThreadSafe()
    {
        var tasks = new List<Task>();

        // 10 threads writing and reading
        for (int i = 0; i < 10; i++)
        {
            var threadId = i;
            tasks.Add(Task.Run(async () =>
            {
                await _cache.SetAsync($"key-{threadId}", $"value-{threadId}");
                var result = await _cache.GetAsync<string>($"key-{threadId}");
                Assert.Equal($"value-{threadId}", result);
            }));
        }

        await Task.WhenAll(tasks);

        var stats = await _cache.GetStatsAsync();
        Assert.Equal(10, stats.TotalEntries);
    }
}
