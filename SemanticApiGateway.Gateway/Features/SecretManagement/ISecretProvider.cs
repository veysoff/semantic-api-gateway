namespace SemanticApiGateway.Gateway.Features.SecretManagement;

/// <summary>
/// Result of attempting to retrieve a secret.
/// </summary>
public class SecretResult
{
    public bool Found { get; set; }
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Service for retrieving and managing secrets in a pluggable way.
/// Supports multiple backends: Azure Key Vault, local secrets, environment variables.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Retrieves a secret by name.
    /// Throws KeyNotFoundException if secret doesn't exist.
    /// </summary>
    Task<string> GetSecretAsync(
        string secretName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to retrieve a secret, returning result without throwing.
    /// </summary>
    Task<SecretResult> TryGetSecretAsync(
        string secretName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a secret exists without retrieving it.
    /// Used to validate configuration before startup.
    /// </summary>
    Task<bool> SecretExistsAsync(
        string secretName,
        CancellationToken cancellationToken = default);
}
