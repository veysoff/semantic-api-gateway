using Microsoft.SemanticKernel;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Models;

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
            OpenApiDocument? parsedDocument = null;
            try
            {
                var reader = new OpenApiStringReader();
                parsedDocument = reader.Read(openApiDocument, out var diagnostic);

                if (diagnostic.Errors.Count > 0)
                {
                    _logger.LogWarning("OpenAPI parsing errors for {ServiceName}: {ErrorCount}",
                        serviceName, diagnostic.Errors.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse OpenAPI document for {ServiceName}", serviceName);
            }

            // Convert OpenAPI paths to kernel functions
            var functions = new List<KernelFunction>();

            if (parsedDocument?.Paths != null)
            {
                foreach (var pathItem in parsedDocument.Paths)
                {
                    string pathKey = pathItem.Key;
                    OpenApiPathItem pathValue = pathItem.Value;

                    foreach (var operation in pathValue.Operations)
                    {
                        OpenApiOperation operationDetails = operation.Value;
                        var function = CreateKernelFunctionFromOperation(serviceName, pathKey, operationDetails);
                        if (function != null)
                        {
                            functions.Add(function);
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
        string pathKey,
        OpenApiOperation operation)
    {
        try
        {
            if (string.IsNullOrEmpty(operation.OperationId))
                return null;

            var functionName = $"{serviceName}_{operation.OperationId}";
            var finalDescription = !string.IsNullOrEmpty(operation.Summary) ? operation.Summary :
                                   (!string.IsNullOrEmpty(operation.Description) ? operation.Description : "No description provided");

            // Create a placeholder function that would be invoked
            // In production, this would create proper function signatures with parameter binding from operation.Parameters
            var method = (string input) => Task.FromResult($"Called {functionName} with input: {input}");
            var function = KernelFunctionFactory.CreateFromMethod(
                method: method,
                functionName: functionName,
                description: finalDescription
            );

            return function;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create kernel function from OpenAPI operation {OperationId} at {PathKey}",
                operation.OperationId, pathKey);
            return null;
        }
    }
}
