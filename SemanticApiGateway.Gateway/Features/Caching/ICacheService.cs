namespace SemanticApiGateway.Gateway.Features.Caching;

/// <summary>
/// Generic cache service interface with TTL support.
/// Implementations: in-memory (default) and Redis (distributed).
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Retrieves value from cache.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets value in cache with optional TTL.
    /// </summary>
    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes specific key from cache.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears entire cache.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    Task<CacheStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Cache statistics for monitoring.
/// </summary>
public class CacheStats
{
    public int TotalEntries { get; set; }
    public long CacheSize { get; set; }
    public int Hits { get; set; }
    public int Misses { get; set; }
    public double HitRate => (Hits + Misses) > 0 ? (double)Hits / (Hits + Misses) : 0;
}

/// <summary>
/// Cached intent result with metadata.
/// </summary>
public class CachedIntentResult
{
    public string Key { get; set; } = string.Empty;
    public object Result { get; set; } = null!;
    public DateTime CachedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int AccessCount { get; set; }
}
