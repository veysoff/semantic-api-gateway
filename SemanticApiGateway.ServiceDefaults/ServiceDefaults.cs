using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SemanticApiGateway.ServiceDefaults;

/// <summary>
/// Extension methods for shared service configuration across all projects
/// </summary>
public static class ServiceDefaults
{
    /// <summary>
    /// Add standard service defaults including health checks, OpenTelemetry, and service discovery
    /// </summary>
    public static IServiceCollection AddServiceDefaults(this IServiceCollection services)
    {
        // Add health checks
        services.AddHealthChecks();

        // Add OpenTelemetry tracing
        services
            .AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddConsoleExporter();
            });

        // Add OpenTelemetry metrics
        services
            .AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddConsoleExporter();
            });

        // Add service discovery and HTTP client defaults
        // Note: Service discovery is configured in the Aspire AppHost
        services.ConfigureHttpClientDefaults(_ =>
        {
            // HTTP client default configuration
            // Service discovery routing will be handled at the Aspire orchestration layer
        });

        return services;
    }
}
