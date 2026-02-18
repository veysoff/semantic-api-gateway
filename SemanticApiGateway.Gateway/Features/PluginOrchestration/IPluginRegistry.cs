using Microsoft.SemanticKernel;

namespace SemanticApiGateway.Gateway.Features.PluginOrchestration;

/// <summary>
/// Manages dynamic plugin registration and retrieval
/// Provides thread-safe access to loaded plugins from microservices
/// </summary>
public interface IPluginRegistry
{
    /// <summary>
    /// Register a plugin from a microservice
    /// </summary>
    /// <param name="serviceName">Name of the microservice (e.g., "OrderService")</param>
    /// <param name="functions">Collection of KernelFunction instances from the service</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RegisterPluginAsync(string serviceName, IEnumerable<KernelFunction> functions, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all registered plugins for a service
    /// </summary>
    /// <param name="serviceName">Name of the microservice</param>
    /// <returns>Collection of registered functions or empty if service not found</returns>
    IReadOnlyList<KernelFunction> GetPlugins(string serviceName);

    /// <summary>
    /// Get all registered services
    /// </summary>
    IReadOnlyList<string> GetRegisteredServices();

    /// <summary>
    /// Check if a service has registered plugins
    /// </summary>
    bool HasService(string serviceName);

    /// <summary>
    /// Clear all plugins for a service (used during refresh)
    /// </summary>
    Task ClearServiceAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear all registered plugins
    /// </summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get metadata about registered plugins
    /// </summary>
    IReadOnlyDictionary<string, int> GetPluginMetadata();
}
