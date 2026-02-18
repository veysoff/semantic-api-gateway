using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SemanticApiGateway.Gateway.Features.Security;
using Xunit;

namespace SemanticApiGateway.Tests.SecurityTests;

/// <summary>
/// Integration tests verifying that IHttpContextAccessor is properly registered
/// and available for TokenPropagationHandler dependency injection
/// </summary>
public class TokenPropagationDependencyInjectionTests
{
    [Fact]
    public void DependencyContainer_WithHttpContextAccessor_ResolvesTokenPropagationHandler()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        // Add the critical dependency that was missing
        builder.Services.AddHttpContextAccessor();

        // Register the dependencies for TokenPropagationHandler
        builder.Services.AddScoped<ITokenPropagationService, TokenPropagationService>();
        builder.Services.AddTransient<TokenPropagationHandler>();

        var serviceProvider = builder.Services.BuildServiceProvider();

        // Act - This should not throw
        var handler = serviceProvider.GetRequiredService<TokenPropagationHandler>();

        // Assert
        handler.Should().NotBeNull();
        handler.Should().BeOfType<TokenPropagationHandler>();
    }

    [Fact]
    public void DependencyContainer_WithoutHttpContextAccessor_ThrowsException()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        // Intentionally skip AddHttpContextAccessor to simulate the bug
        builder.Services.AddScoped<ITokenPropagationService, TokenPropagationService>();
        builder.Services.AddTransient<TokenPropagationHandler>();

        var serviceProvider = builder.Services.BuildServiceProvider();

        // Act & Assert
        var act = () => serviceProvider.GetRequiredService<TokenPropagationHandler>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IHttpContextAccessor*");
    }

    [Fact]
    public void GatewayHttpClient_WithPropagationHandler_CanBeResolved()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();

        // Simulate the full Program.cs setup
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITokenPropagationService, TokenPropagationService>();
        builder.Services.AddTransient<TokenPropagationHandler>();

        // Register the HttpClient with the handler
        builder.Services.AddHttpClient("gateway")
            .AddHttpMessageHandler<TokenPropagationHandler>();

        var serviceProvider = builder.Services.BuildServiceProvider();

        // Act - This should not throw
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("gateway");

        // Assert
        httpClient.Should().NotBeNull();
    }

    [Fact]
    public void HttpContextAccessor_IsRegisteredAsScoped()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHttpContextAccessor();

        var serviceProvider = builder.Services.BuildServiceProvider();

        // Act
        var accessor1 = serviceProvider.GetRequiredService<IHttpContextAccessor>();
        var accessor2 = serviceProvider.GetRequiredService<IHttpContextAccessor>();

        // Assert
        accessor1.Should().NotBeNull();
        accessor2.Should().NotBeNull();
        // Both should be the same instance within the same scope
        accessor1.Should().Be(accessor2);
    }
}

/// <summary>
/// Security-focused tests for TokenPropagationService
/// Verifies that JWT tokens are properly extracted from HTTP context
/// </summary>
public class TokenPropagationServiceTests
{
    private readonly TokenPropagationService _service;

    public TokenPropagationServiceTests()
    {
        // Setup configuration with default test values
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Auth:Issuer", "https://localhost:5001" },
                { "Auth:Audience", "api://semantic-gateway" },
                { "Auth:SecretKey", "super-secret-key-change-in-production-minimum-32-characters" }
            })
            .Build();

        var loggerMock = new Mock<ILogger<TokenPropagationService>>();
        _service = new TokenPropagationService(loggerMock.Object, config);
    }

    [Fact]
    public void ExtractToken_WithBearerToken_ReturnsToken()
    {
        // Arrange
        const string token = "test-jwt-token-12345";
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {token}";

        // Act
        var result = _service.ExtractToken(httpContext);

        // Assert
        result.Should().Be(token);
    }

    [Fact]
    public void ExtractToken_WithoutBearerScheme_ReturnsNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        // Act
        var result = _service.ExtractToken(httpContext);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractToken_WithoutAuthorizationHeader_ReturnsNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();

        // Act
        var result = _service.ExtractToken(httpContext);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractToken_WithMalformedBearerHeader_ReturnsNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer";

        // Act
        var result = _service.ExtractToken(httpContext);

        // Assert
        result.Should().BeNull();
    }
}
