using System.Collections.Concurrent;

namespace SemanticApiGateway.Gateway.Features.SecretManagement;

/// <summary>
/// Service for monitoring and refreshing rotated secrets.
/// Detects when secrets have been updated in Key Vault and invalidates local caches.
/// </summary>
public interface ISecretRotationService
{
    Task NotifySecretRotatedAsync(string secretName);
    Task<DateTime> GetLastRotationTimeAsync(string secretName);
    Task<bool> IsSecretRotatedSinceAsync(string secretName, DateTime since);
}

/// <summary>
/// In-memory implementation tracking secret rotation events.
/// </summary>
public class SecretRotationService(
    ILogger<SecretRotationService> logger) : ISecretRotationService
{
    private readonly ConcurrentDictionary<string, RotationEvent> _rotations = new();

    public async Task NotifySecretRotatedAsync(string secretName)
    {
        var rotationEvent = new RotationEvent
        {
            SecretName = secretName,
            RotatedAt = DateTime.UtcNow
        };

        _rotations.AddOrUpdate(secretName, rotationEvent, (_, _) => rotationEvent);
        logger.LogInformation("Secret rotation recorded: {SecretName} at {RotatedAt}",
            secretName, rotationEvent.RotatedAt);

        await Task.CompletedTask;
    }

    public async Task<DateTime> GetLastRotationTimeAsync(string secretName)
    {
        if (_rotations.TryGetValue(secretName, out var rotation))
        {
            return await Task.FromResult(rotation.RotatedAt);
        }

        return await Task.FromResult(DateTime.UnixEpoch);
    }

    public async Task<bool> IsSecretRotatedSinceAsync(string secretName, DateTime since)
    {
        if (_rotations.TryGetValue(secretName, out var rotation))
        {
            var isRotated = rotation.RotatedAt >= since;
            return await Task.FromResult(isRotated);
        }

        return await Task.FromResult(false);
    }
}

internal class RotationEvent
{
    public string SecretName { get; set; } = string.Empty;
    public DateTime RotatedAt { get; set; }
}
