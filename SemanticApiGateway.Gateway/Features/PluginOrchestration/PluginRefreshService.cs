using SemanticApiGateway.Gateway.Configuration;
using Microsoft.Extensions.Options;

namespace SemanticApiGateway.Gateway.Features.PluginOrchestration;

/// <summary>
/// Background service that periodically refreshes plugins from microservices
/// Discovers new plugins and updates plugin metadata
/// </summary>
public class PluginRefreshService : BackgroundService
{
    private readonly IPluginRegistry _pluginRegistry;
    private readonly IOpenApiPluginLoader _pluginLoader;
    private readonly GatewayOptions _options;
    private readonly ILogger<PluginRefreshService> _logger;
    private readonly ServiceDiscoveryOptions _serviceDiscovery;

    public PluginRefreshService(
        IPluginRegistry pluginRegistry,
        IOpenApiPluginLoader pluginLoader,
        IOptions<GatewayOptions> options,
        ILogger<PluginRefreshService> logger)
    {
        _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
        _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceDiscovery = _options.ServiceDiscovery;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PluginRefresh.Enabled)
        {
            _logger.LogInformation("Plugin refresh service is disabled");
            return;
        }

        _logger.LogInformation("Plugin refresh service started with interval of {Seconds} seconds",
            _options.PluginRefresh.IntervalSeconds);

        // Initial load
        await RefreshAllPluginsAsync(stoppingToken);

        // Periodic refresh
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PluginRefresh.IntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RefreshAllPluginsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Plugin refresh service stopped");
        }
    }

    /// <summary>
    /// Refresh plugins from all registered microservices
    /// </summary>
    private async Task RefreshAllPluginsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Starting plugin refresh cycle");

            var services = new Dictionary<string, string>
            {
                { "OrderService", $"{_serviceDiscovery.OrderServiceUrl}/swagger/v1/swagger.json" },
                { "InventoryService", $"{_serviceDiscovery.InventoryServiceUrl}/swagger/v1/swagger.json" },
                { "UserService", $"{_serviceDiscovery.UserServiceUrl}/swagger/v1/swagger.json" }
            };

            foreach (var (serviceName, swaggerUrl) in services)
            {
                await RefreshServicePluginsAsync(serviceName, swaggerUrl, cancellationToken);
            }

            // Log plugin metadata
            var metadata = _pluginRegistry.GetPluginMetadata();
            _logger.LogInformation("Plugin refresh completed. Registered services: {Services}",
                string.Join(", ", metadata.Select(m => $"{m.Key}({m.Value})")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin refresh cycle");
        }
    }

    /// <summary>
    /// Refresh plugins for a specific service
    /// </summary>
    private async Task RefreshServicePluginsAsync(
        string serviceName,
        string swaggerUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Refreshing plugins for {ServiceName}", serviceName);

            // Check if endpoint is accessible
            var isAccessible = await _pluginLoader.ValidateEndpointAsync(swaggerUrl, cancellationToken);

            if (!isAccessible)
            {
                _logger.LogWarning("Service {ServiceName} at {SwaggerUrl} is not accessible",
                    serviceName, swaggerUrl);
                await _pluginRegistry.ClearServiceAsync(serviceName, cancellationToken);
                return;
            }

            // Load plugins from the service
            var plugins = await _pluginLoader.LoadPluginsAsync(serviceName, swaggerUrl, cancellationToken);

            if (plugins.Any())
            {
                await _pluginRegistry.RegisterPluginAsync(serviceName, plugins, cancellationToken);
                _logger.LogInformation("Loaded {PluginCount} plugins from {ServiceName}",
                    plugins.Count(), serviceName);
            }
            else
            {
                _logger.LogWarning("No plugins loaded from {ServiceName}", serviceName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh plugins for {ServiceName}", serviceName);
        }
    }
}
