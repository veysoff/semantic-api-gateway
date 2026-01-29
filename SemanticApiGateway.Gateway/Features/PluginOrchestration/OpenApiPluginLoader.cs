using Microsoft.SemanticKernel;
using Microsoft.OpenApi.Readers;
using System.Text.Json;

namespace SemanticApiGateway.Gateway.Features.PluginOrchestration;

/// <summary>
/// Loads plugins from microservices via OpenAPI/Swagger specifications
/// Parses API specs and converts them into Semantic Kernel functions
/// </summary>
public class OpenApiPluginLoader : IOpenApiPluginLoader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenApiPluginLoader> _logger;

    public OpenApiPluginLoader(HttpClient httpClient, ILogger<OpenApiPluginLoader> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEnumerable<KernelFunction>> LoadPluginsAsync(
        string serviceName,
        string swaggerUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentNullException(nameof(serviceName));

        if (string.IsNullOrWhiteSpace(swaggerUrl))
            throw new ArgumentNullException(nameof(swaggerUrl));

        try
        {
            _logger.LogInformation("Loading plugins from {ServiceName} at {SwaggerUrl}", serviceName, swaggerUrl);

            // Fetch the OpenAPI document
            var openApiDocument = await FetchOpenApiDocumentAsync(swaggerUrl, cancellationToken);

            if (string.IsNullOrEmpty(openApiDocument))
            {
                _logger.LogWarning("Empty OpenAPI document from {ServiceName}", serviceName);
                return Array.Empty<KernelFunction>();
            }

            // Parse the OpenAPI specification
            var reader = new OpenApiStringReader();
            var parsedDocument = reader.Read(openApiDocument, out var diagnostic);

            if (diagnostic.Errors.Count > 0)
            {
                _logger.LogWarning("OpenAPI parsing errors for {ServiceName}: {Errors}",
                    serviceName, string.Join(", ", diagnostic.Errors.Select(e => e.Message)));
            }

            // Convert OpenAPI paths to kernel functions
            var functions = new List<KernelFunction>();

            if (parsedDocument?.Paths != null)
            {
                foreach (var path in parsedDocument.Paths)
                {
                    foreach (var operation in path.Value.Operations.Values)
                    {
                        if (operation != null)
                        {
                            var function = CreateKernelFunctionFromOperation(serviceName, path.Key, operation);
                            if (function != null)
                            {
                                functions.Add(function);
                            }
                        }
                    }
                }
            }

            _logger.LogInformation("Loaded {FunctionCount} functions from {ServiceName}",
                functions.Count, serviceName);

            return functions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugins from {ServiceName} at {SwaggerUrl}",
                serviceName, swaggerUrl);
            return Array.Empty<KernelFunction>();
        }
    }

    public async Task<bool> ValidateEndpointAsync(string swaggerUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(swaggerUrl))
            return false;

        try
        {
            var response = await _httpClient.GetAsync(swaggerUrl, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate endpoint {SwaggerUrl}", swaggerUrl);
            return false;
        }
    }

    public async Task<string> FetchOpenApiDocumentAsync(string swaggerUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(swaggerUrl))
            throw new ArgumentNullException(nameof(swaggerUrl));

        try
        {
            _logger.LogDebug("Fetching OpenAPI document from {SwaggerUrl}", swaggerUrl);

            var response = await _httpClient.GetAsync(swaggerUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch OpenAPI document from {SwaggerUrl}", swaggerUrl);
            throw;
        }
    }

    /// <summary>
    /// Create a Semantic Kernel function from an OpenAPI operation
    /// This is a simplified version - production would need more sophisticated mapping
    /// </summary>
    private KernelFunction? CreateKernelFunctionFromOperation(
        string serviceName,
        string path,
        Microsoft.OpenApi.Models.OpenApiOperation operation)
    {
        try
        {
            if (string.IsNullOrEmpty(operation.OperationId))
                return null;

            var functionName = $"{serviceName}_{operation.OperationId}";
            var description = operation.Summary ?? operation.Description ?? "No description provided";

            // For now, create a placeholder function that would be invoked
            // In production, this would create proper function signatures with parameter binding
            var function = KernelFunctionFactory.CreateFromMethod(
                method: (string input) => Task.FromResult($"Called {functionName} with input: {input}"),
                functionName: functionName,
                description: description
            );

            return function;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create kernel function for operation {OperationId}",
                operation.OperationId);
            return null;
        }
    }
}
