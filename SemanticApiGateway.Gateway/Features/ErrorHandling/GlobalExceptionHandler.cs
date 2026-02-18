using Microsoft.AspNetCore.Diagnostics;
using System.Diagnostics;
using SemanticApiGateway.Gateway.Models;
using System.Text.Json;

namespace SemanticApiGateway.Gateway.Features.ErrorHandling;

/// <summary>
/// Global exception handler for .NET 10 minimal APIs.
/// Catches all unhandled exceptions and returns standardized error responses.
/// Integrates with structured logging and distributed tracing.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const string TraceIdHeader = "X-Trace-Id";
    private const string CorrelationIdHeader = "X-Correlation-Id";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        var correlationId = GetOrCreateCorrelationId(httpContext);

        logger.LogError(
            exception,
            "Unhandled exception occurred. TraceId: {TraceId}, CorrelationId: {CorrelationId}, Path: {Path}, Method: {Method}",
            traceId,
            correlationId,
            httpContext.Request.Path,
            httpContext.Request.Method);

        var errorResponse = MapExceptionToResponse(exception, traceId, correlationId);

        httpContext.Response.ContentType = "application/json";
        httpContext.Response.StatusCode = errorResponse.StatusCode;
        httpContext.Response.Headers.TryAdd(TraceIdHeader, traceId);
        httpContext.Response.Headers.TryAdd(CorrelationIdHeader, correlationId);

        await httpContext.Response.WriteAsJsonAsync(
            new
            {
                error = errorResponse.Error,
                details = errorResponse.Details,
                traceId,
                correlationId,
                timestamp = DateTime.UtcNow.ToString("O"),
                path = httpContext.Request.Path.ToString()
            },
            cancellationToken);

        return true;
    }

    private ErrorResponse MapExceptionToResponse(Exception exception, string traceId, string correlationId)
    {
        return exception switch
        {
            // Argument validation exceptions
            ArgumentNullException or ArgumentException =>
                new ErrorResponse
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Error = "Invalid Request",
                    Details = $"Request validation failed: {exception.Message}"
                },

            // Authentication exceptions
            UnauthorizedAccessException =>
                new ErrorResponse
                {
                    StatusCode = StatusCodes.Status401Unauthorized,
                    Error = "Unauthorized",
                    Details = "Authentication failed or token is invalid"
                },

            // Not found exceptions
            KeyNotFoundException =>
                new ErrorResponse
                {
                    StatusCode = StatusCodes.Status404NotFound,
                    Error = "Not Found",
                    Details = exception.Message
                },

            // Operation timeout
            OperationCanceledException =>
                new ErrorResponse
                {
                    StatusCode = StatusCodes.Status408RequestTimeout,
                    Error = "Request Timeout",
                    Details = "The operation took too long to complete. Please try again."
                },

            // HTTP request exceptions from downstream services
            HttpRequestException httpEx =>
                MapHttpRequestException(httpEx),

            // Generic exceptions
            _ =>
                new ErrorResponse
                {
                    StatusCode = StatusCodes.Status500InternalServerError,
                    Error = "Internal Server Error",
                    Details = "An unexpected error occurred. Please contact support with trace ID: " + traceId
                }
        };
    }

    private ErrorResponse MapHttpRequestException(HttpRequestException exception)
    {
        var message = exception.Message.ToLowerInvariant();

        // Timeout or connection issues
        if (message.Contains("timeout") || message.Contains("connection"))
        {
            return new ErrorResponse
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                Error = "Service Unavailable",
                Details = "Unable to connect to downstream service. Please try again later."
            };
        }

        // DNS resolution failures
        if (message.Contains("dns") || message.Contains("name resolution"))
        {
            return new ErrorResponse
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                Error = "Service Unavailable",
                Details = "Unable to resolve service endpoint."
            };
        }

        // Default HTTP error
        return new ErrorResponse
        {
            StatusCode = StatusCodes.Status502BadGateway,
            Error = "Bad Gateway",
            Details = "Error communicating with downstream service."
        };
    }

    private string GetOrCreateCorrelationId(HttpContext httpContext)
    {
        const string correlationIdKey = "CorrelationId";

        if (httpContext.Items.TryGetValue(correlationIdKey, out var correlationId))
        {
            return correlationId?.ToString() ?? Guid.NewGuid().ToString();
        }

        var newCorrelationId = Guid.NewGuid().ToString();
        httpContext.Items[correlationIdKey] = newCorrelationId;
        return newCorrelationId;
    }
}
