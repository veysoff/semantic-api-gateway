using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace SemanticApiGateway.Gateway.Features.SecretManagement;

/// <summary>
/// Azure Key Vault implementation for production secret management.
/// Requires Azure.Security.KeyVault.Secrets NuGet package and Azure credentials.
/// </summary>
public class KeyVaultSecretProvider(
    ILogger<KeyVaultSecretProvider> logger) : ISecretProvider
{
    private SecretClient? _client;

    private SecretClient Client
    {
        get
        {
            _client ??= InitializeClient();
            return _client;
        }
    }

    private SecretClient InitializeClient()
    {
        var keyVaultUrl = Environment.GetEnvironmentVariable("AZURE_KEYVAULT_URL");

        if (string.IsNullOrEmpty(keyVaultUrl))
        {
            throw new InvalidOperationException(
                "AZURE_KEYVAULT_URL environment variable is not configured. " +
                "Set it to your Key Vault URL (e.g., https://myvault.vault.azure.net/)");
        }

        var credential = new DefaultAzureCredential();
        logger.LogInformation("Initializing Key Vault connection to {KeyVaultUrl}", keyVaultUrl);
        return new SecretClient(new Uri(keyVaultUrl), credential);
    }

    public async Task<string> GetSecretAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var secret = await Client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            logger.LogInformation("Retrieved secret from Key Vault: {SecretName}", secretName);
            return secret.Value.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            logger.LogWarning("Secret not found in Key Vault: {SecretName}", secretName);
            throw new KeyNotFoundException($"Secret '{secretName}' not found in Key Vault", ex);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving secret from Key Vault: {SecretName}", secretName);
            throw;
        }
    }

    public async Task<SecretResult> TryGetSecretAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var secret = await Client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            logger.LogInformation("Retrieved secret from Key Vault: {SecretName}", secretName);
            return new SecretResult { Found = true, Value = secret.Value.Value };
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            logger.LogDebug("Secret not found in Key Vault: {SecretName}", secretName);
            return new SecretResult { Found = false, Value = string.Empty };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving secret from Key Vault: {SecretName}", secretName);
            return new SecretResult { Found = false, Value = string.Empty };
        }
    }

    public async Task<bool> SecretExistsAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            return true;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking secret existence: {SecretName}", secretName);
            return false;
        }
    }
}
