namespace SemanticApiGateway.Gateway.Configuration;

/// <summary>
/// Configuration options for the Gateway
/// </summary>
public class GatewayOptions
{
    public const string SectionName = "SemanticKernel";

    public FunctionsOptions Functions { get; set; } = new();
    public AuthOptions Auth { get; set; } = new();
    public ServiceDiscoveryOptions ServiceDiscovery { get; set; } = new();
    public PluginRefreshOptions PluginRefresh { get; set; } = new();
    public RateLimitOptions RateLimit { get; set; } = new();
}

public class FunctionsOptions
{
    public int TimeoutMs { get; set; } = 30000;
    public int MaxRetries { get; set; } = 3;
}

public class AuthOptions
{
    public string Authority { get; set; } = "https://localhost:5001";
    public string Audience { get; set; } = "api://semantic-gateway";
}

public class ServiceDiscoveryOptions
{
    public string OrderServiceUrl { get; set; } = "http://localhost:5100";
    public string InventoryServiceUrl { get; set; } = "http://localhost:5200";
    public string UserServiceUrl { get; set; } = "http://localhost:5300";
}

public class PluginRefreshOptions
{
    public int IntervalSeconds { get; set; } = 60;
    public bool Enabled { get; set; } = true;
}

public class RateLimitOptions
{
    public int RequestsPerHour { get; set; } = 100;
    public bool Enabled { get; set; } = true;
}
