namespace SemanticApiGateway.Gateway.Features.SecretManagement;

/// <summary>
/// Development/local implementation using IConfiguration.
/// Reads from appsettings.json and appsettings.Development.json.
/// Safe for local development, not for production.
/// </summary>
public class LocalSecretProvider(
    IConfiguration configuration,
    ILogger<LocalSecretProvider> logger) : ISecretProvider
{
    public async Task<string> GetSecretAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        var value = configuration[$"Secrets:{secretName}"];

        if (string.IsNullOrEmpty(value))
        {
            logger.LogWarning("Secret not found: {SecretName}", secretName);
            throw new KeyNotFoundException($"Secret '{secretName}' not found in configuration");
        }

        logger.LogInformation("Retrieved secret from local configuration: {SecretName}", secretName);
        return await Task.FromResult(value);
    }

    public async Task<SecretResult> TryGetSecretAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        var value = configuration[$"Secrets:{secretName}"] ?? string.Empty;
        var found = !string.IsNullOrEmpty(value);

        if (found)
        {
            logger.LogInformation("Retrieved secret from local configuration: {SecretName}", secretName);
        }

        return await Task.FromResult(new SecretResult { Found = found, Value = value });
    }

    public async Task<bool> SecretExistsAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        var exists = !string.IsNullOrEmpty(configuration[$"Secrets:{secretName}"]);
        return await Task.FromResult(exists);
    }
}
