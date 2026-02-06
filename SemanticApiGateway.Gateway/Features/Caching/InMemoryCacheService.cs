using System.Collections.Concurrent;
using System.Text.Json;

namespace SemanticApiGateway.Gateway.Features.Caching;

/// <summary>
/// In-memory cache implementation with TTL and size limits.
/// Suitable for single-instance deployments. Use Redis for distributed.
/// </summary>
public class InMemoryCacheService(ILogger<InMemoryCacheService> logger) : ICacheService
{
    private const int MaxEntries = 1000;
    private const int MaxSizeBytes = 100 * 1024 * 1024; // 100 MB

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private long _totalSize = 0;
    private int _hits = 0;
    private int _misses = 0;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired())
            {
                _cache.TryRemove(key, out _);
                _misses++;
                logger.LogDebug("Cache miss (expired): {Key}", key);
                return await Task.FromResult<T?>(default);
            }

            entry.AccessCount++;
            _hits++;
            logger.LogDebug("Cache hit: {Key} (access #{AccessCount})", key, entry.AccessCount);
            return await Task.FromResult(JsonSerializer.Deserialize<T>(entry.SerializedValue, _jsonOptions));
        }

        _misses++;
        logger.LogDebug("Cache miss: {Key}", key);
        return await Task.FromResult<T?>(default);
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        var serialized = JsonSerializer.Serialize(value, _jsonOptions);
        var entrySize = System.Text.Encoding.UTF8.GetByteCount(serialized);

        // Check cache size limits
        if (_cache.Count >= MaxEntries)
        {
            EvictLeastUsed();
        }

        if (_totalSize + entrySize > MaxSizeBytes)
        {
            EvictBySize();
        }

        var entry = new CacheEntry
        {
            Key = key,
            SerializedValue = serialized,
            Size = entrySize,
            CachedAt = DateTime.UtcNow,
            ExpiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : null,
            AccessCount = 0
        };

        if (_cache.TryGetValue(key, out var existing))
        {
            Interlocked.Add(ref _totalSize, -existing.Size);
        }

        _cache.AddOrUpdate(key, entry, (_, _) => entry);
        Interlocked.Add(ref _totalSize, entrySize);

        logger.LogDebug(
            "Cache set: {Key} ({SizeKB}KB, TTL: {TTL})",
            key,
            entrySize / 1024,
            ttl?.TotalSeconds ?? 0);

        await Task.CompletedTask;
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_cache.TryRemove(key, out var entry))
        {
            Interlocked.Add(ref _totalSize, -entry.Size);
            logger.LogDebug("Cache removed: {Key}", key);
        }

        await Task.CompletedTask;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var count = _cache.Count;
        _cache.Clear();
        _totalSize = 0;
        _hits = 0;
        _misses = 0;
        logger.LogInformation("Cache cleared ({EntryCount} entries)", count);

        await Task.CompletedTask;
    }

    public async Task<CacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(new CacheStats
        {
            TotalEntries = _cache.Count,
            CacheSize = _totalSize,
            Hits = _hits,
            Misses = _misses
        });
    }

    private void EvictLeastUsed()
    {
        if (_cache.IsEmpty) return;

        var leastUsed = _cache.Values
            .OrderBy(e => e.AccessCount)
            .ThenBy(e => e.CachedAt)
            .FirstOrDefault();

        if (leastUsed != null)
        {
            _cache.TryRemove(leastUsed.Key, out _);
            Interlocked.Add(ref _totalSize, -leastUsed.Size);
            logger.LogDebug("Evicted least used: {Key} ({AccessCount} accesses)", leastUsed.Key, leastUsed.AccessCount);
        }
    }

    private void EvictBySize()
    {
        var toRemove = _cache.Values
            .OrderBy(e => e.AccessCount)
            .ThenBy(e => e.CachedAt)
            .Take(_cache.Count / 10) // Remove 10% when size exceeded
            .ToList();

        foreach (var entry in toRemove)
        {
            _cache.TryRemove(entry.Key, out _);
            Interlocked.Add(ref _totalSize, -entry.Size);
        }

        logger.LogDebug("Size eviction: removed {Count} entries", toRemove.Count);
    }

    private class CacheEntry
    {
        public string Key { get; set; } = string.Empty;
        public string SerializedValue { get; set; } = string.Empty;
        public int Size { get; set; }
        public DateTime CachedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public int AccessCount { get; set; }

        public bool IsExpired() => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;
    }
}
