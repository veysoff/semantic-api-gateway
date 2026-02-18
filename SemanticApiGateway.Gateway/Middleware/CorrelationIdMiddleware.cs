using System.Diagnostics;

namespace SemanticApiGateway.Gateway.Middleware;

/// <summary>
/// Middleware for managing correlation IDs across distributed systems.
/// Enables end-to-end tracing of requests across multiple services.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private const string TraceIdHeader = "X-Trace-Id";
    private const string CorrelationIdKey = "CorrelationId";
    private const string TraceIdKey = "TraceId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract or create correlation ID from request headers
        var correlationId = ExtractCorrelationId(context.Request);
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        // Store in HttpContext for access in handlers
        context.Items[CorrelationIdKey] = correlationId;
        context.Items[TraceIdKey] = traceId;

        // Add to response headers for client tracking
        context.Response.Headers.TryAdd(CorrelationIdHeader, correlationId);
        context.Response.Headers.TryAdd(TraceIdHeader, traceId);

        // Log request with correlation ID
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = traceId,
            ["RequestPath"] = context.Request.Path.ToString(),
            ["RequestMethod"] = context.Request.Method
        }))
        {
            _logger.LogInformation(
                "Request started: {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            try
            {
                await _next(context);
            }
            finally
            {
                _logger.LogInformation(
                    "Request completed: {Method} {Path} - Status: {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode);
            }
        }
    }

    private static string ExtractCorrelationId(HttpRequest request)
    {
        // If client provides correlation ID, use it
        if (request.Headers.TryGetValue(CorrelationIdHeader, out var correlationIdHeader))
        {
            var clientId = correlationIdHeader.ToString();
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                return clientId;
            }
        }

        // Generate new correlation ID if not provided
        return Guid.NewGuid().ToString();
    }
}

/// <summary>
/// Extension methods for registering correlation ID middleware.
/// </summary>
public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationId(
        this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CorrelationIdMiddleware>();
    }
}
