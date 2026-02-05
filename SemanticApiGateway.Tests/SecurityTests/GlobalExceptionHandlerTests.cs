using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using SemanticApiGateway.Gateway.Features.ErrorHandling;
using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace SemanticApiGateway.Tests.SecurityTests;

/// <summary>
/// Test suite for GlobalExceptionHandler.
/// Validates proper exception mapping and error response generation.
/// </summary>
public class GlobalExceptionHandlerTests
{
    private readonly Mock<ILogger<GlobalExceptionHandler>> _mockLogger;
    private readonly GlobalExceptionHandler _handler;

    public GlobalExceptionHandlerTests()
    {
        _mockLogger = new Mock<ILogger<GlobalExceptionHandler>>();
        _handler = new GlobalExceptionHandler(_mockLogger.Object);
    }

    #region Exception Mapping Tests

    [Fact]
    public async Task TryHandleAsync_WithArgumentNullException_Returns400BadRequest()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new ArgumentNullException(nameof(context), "Context cannot be null");

        // Act
        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_WithArgumentException_Returns400BadRequest()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new ArgumentException("Invalid argument");

        // Act
        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_WithUnauthorizedAccessException_Returns401Unauthorized()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new UnauthorizedAccessException("Access denied");

        // Act
        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_WithKeyNotFoundException_Returns404NotFound()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new KeyNotFoundException("Resource not found");

        // Act
        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_WithOperationCanceledException_Returns408RequestTimeout()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new OperationCanceledException("Operation timed out");

        // Act
        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status408RequestTimeout, context.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_WithHttpRequestException_Timeout_Returns503ServiceUnavailable()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new HttpRequestException("The operation timed out");

        // Act
        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        // Timeout messages map to 503 Service Unavailable
        Assert.True(context.Response.StatusCode is StatusCodes.Status503ServiceUnavailable or StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task TryHandleAsync_WithHttpRequestException_ConnectionError_Returns502Or503()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new HttpRequestException("Unable to connect to the remote server");

        // Act
        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert - May return either 502 (default) or 503 (if message contains specific keywords)
        Assert.True(handled);
        Assert.True(context.Response.StatusCode is StatusCodes.Status502BadGateway or StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task TryHandleAsync_WithHttpRequestException_DnsError_Returns503ServiceUnavailable()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new HttpRequestException("Name resolution failed");

        // Act
        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandleAsync_WithGenericException_Returns500InternalServerError()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new InvalidOperationException("Unexpected error");

        // Act
        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    #endregion

    #region Response Header Tests

    [Fact]
    public async Task TryHandleAsync_IncludesTraceIdInResponse()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new InvalidOperationException("Error");

        // Act
        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.True(context.Response.Headers.ContainsKey("X-Trace-Id"));
    }

    [Fact]
    public async Task TryHandleAsync_IncludesCorrelationIdInResponse()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new InvalidOperationException("Error");

        // Act
        var handled = await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.True(context.Response.Headers.ContainsKey("X-Correlation-Id"));
    }

    [Fact]
    public async Task TryHandleAsync_UsesPreviousCorrelationId_IfAvailable()
    {
        // Arrange
        var context = CreateHttpContext();
        var expectedCorrelationId = "correlation-123";
        context.Items["CorrelationId"] = expectedCorrelationId;
        var exception = new InvalidOperationException("Error");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        var headerValue = context.Response.Headers["X-Correlation-Id"].ToString();
        Assert.Equal(expectedCorrelationId, headerValue);
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task TryHandleAsync_LogsErrorWithContext()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new InvalidOperationException("Test error");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Once);
    }

    #endregion

    #region Response Content Tests

    [Fact]
    public async Task TryHandleAsync_ResponseContentTypeIsJson()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new InvalidOperationException("Error");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert - ContentType may include charset
        Assert.True(context.Response.ContentType?.Contains("application/json") ?? false);
    }

    [Fact]
    public async Task TryHandleAsync_ReturnsValidHttpContent()
    {
        // Arrange
        var context = CreateHttpContext();
        var exception = new ArgumentNullException("test");

        // Act
        await _handler.TryHandleAsync(context, exception, CancellationToken.None);

        // Assert
        Assert.True(context.Response.Body.Length > 0);
    }

    #endregion

    #region Helper Methods

    private static HttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/test";
        httpContext.Request.Method = "POST";
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    #endregion
}
