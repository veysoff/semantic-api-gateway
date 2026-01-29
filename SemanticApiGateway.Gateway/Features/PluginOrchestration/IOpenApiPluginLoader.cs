using Microsoft.SemanticKernel;

namespace SemanticApiGateway.Gateway.Features.PluginOrchestration;

/// <summary>
/// Loads Semantic Kernel plugins from OpenAPI/Swagger specifications
/// Converts microservice API specs into callable kernel functions
/// </summary>
public interface IOpenApiPluginLoader
{
    /// <summary>
    /// Load plugins from a microservice's OpenAPI/Swagger endpoint
    /// </summary>
    /// <param name="serviceName">Name of the service (e.g., "OrderService")</param>
    /// <param name="swaggerUrl">URL to the service's Swagger/OpenAPI endpoint</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of loaded KernelFunction instances</returns>
    Task<IEnumerable<KernelFunction>> LoadPluginsAsync(string serviceName, string swaggerUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate that a Swagger/OpenAPI endpoint is accessible
    /// </summary>
    /// <param name="swaggerUrl">URL to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if endpoint is accessible and valid</returns>
    Task<bool> ValidateEndpointAsync(string swaggerUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the OpenAPI document from a service
    /// </summary>
    /// <param name="swaggerUrl">URL to the Swagger/OpenAPI endpoint</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>OpenAPI document as string</returns>
    Task<string> FetchOpenApiDocumentAsync(string swaggerUrl, CancellationToken cancellationToken = default);
}
