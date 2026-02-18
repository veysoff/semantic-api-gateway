using SemanticApiGateway.Gateway.Features.AuditTrail;

namespace SemanticApiGateway.Gateway.Middleware;

/// <summary>
/// Middleware for automatic audit logging of HTTP requests.
/// Records all API calls with user, action, and result information.
/// </summary>
public class AuditTrailMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditTrailMiddleware> _logger;

    public AuditTrailMiddleware(
        RequestDelegate next,
        ILogger<AuditTrailMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAuditService auditService)
    {
        var originalBodyStream = context.Response.Body;

        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        var userId = ExtractUserId(context);
        var startTime = DateTime.UtcNow;

        try
        {
            await _next(context);

            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var action = GetActionFromMethod(context.Request.Method);
            var resource = context.Request.Path.Value ?? "/";

            if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
            {
                await auditService.LogActionAsync(
                    userId,
                    action,
                    resource,
                    context.Request.Method,
                    context.Response.StatusCode);
            }
            else if (context.Response.StatusCode >= 400)
            {
                var errorMessage = $"HTTP {context.Response.StatusCode}";
                await auditService.LogErrorAsync(
                    userId,
                    action,
                    resource,
                    context.Request.Method,
                    context.Response.StatusCode,
                    errorMessage);
            }

            _logger.LogDebug(
                "Audit: {Method} {Path} - {StatusCode} ({DurationMs}ms)",
                context.Request.Method,
                resource,
                context.Response.StatusCode,
                duration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in audit trail middleware");
            throw;
        }
        finally
        {
            memoryStream.Seek(0, SeekOrigin.Begin);
            await memoryStream.CopyToAsync(originalBodyStream);
            context.Response.Body = originalBodyStream;
        }
    }

    private static string ExtractUserId(HttpContext context)
    {
        var userIdClaim = context.User?.FindFirst("sub")?.Value;
        return userIdClaim ?? context.User?.Identity?.Name ?? "anonymous";
    }

    private static string GetActionFromMethod(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => "read",
            "POST" => "create",
            "PUT" => "update",
            "DELETE" => "delete",
            "PATCH" => "modify",
            _ => "access"
        };
    }
}

/// <summary>
/// Extension for registering audit trail middleware.
/// </summary>
public static class AuditTrailExtensions
{
    public static IApplicationBuilder UseAuditTrail(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AuditTrailMiddleware>();
    }
}
