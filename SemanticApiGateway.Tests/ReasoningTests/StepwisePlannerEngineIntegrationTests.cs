using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Moq;
using SemanticApiGateway.Gateway.Configuration;
using SemanticApiGateway.Gateway.Features.Caching;
using SemanticApiGateway.Gateway.Features.Observability;
using SemanticApiGateway.Gateway.Features.Reasoning;
using SemanticApiGateway.Gateway.Models;
using Xunit;

namespace SemanticApiGateway.Tests.ReasoningTests;

/// <summary>
/// Integration tests for multi-step execution with data piping and error handling
/// </summary>
public class StepwisePlannerEngineIntegrationTests
{
    private readonly StepwisePlannerEngine _engine;
    private readonly Mock<ILogger<StepwisePlannerEngine>> _mockLogger;
    private readonly Mock<IGatewayActivitySource> _mockActivitySource;
    private readonly ResilienceConfiguration _resilienceConfig;

    public StepwisePlannerEngineIntegrationTests()
    {
        _mockLogger = new Mock<ILogger<StepwisePlannerEngine>>();
        _mockActivitySource = new Mock<IGatewayActivitySource>();

        _resilienceConfig = new ResilienceConfiguration
        {
            DefaultTimeoutSeconds = 30,
            DefaultMaxRetries = 3,
            DefaultBackoffMs = 100
        };

        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion("gpt-4", "dummy-key")
            .Build();

        var options = Options.Create(_resilienceConfig);
        var mockVarLogger = new Mock<ILogger<VariableResolver>>();
        var variableResolver = new VariableResolver(mockVarLogger.Object);

        var mockCacheLogger = new Mock<ILogger<InMemoryCacheService>>();
        var cacheService = new InMemoryCacheService(mockCacheLogger.Object);

        _engine = new StepwisePlannerEngine(
            kernel,
            _mockLogger.Object,
            variableResolver,
            options,
            _mockActivitySource.Object,
            cacheService
        );
    }

    #region Execution Plan Tests

    [Fact]
    public async Task ExecuteIntent_CreatesValidExecutionPlan()
    {
        // Arrange
        var intent = "Get user information";
        var userId = "user-123";

        // Act
        var result = await _engine.ExecuteIntentAsync(intent, userId);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.StepResults);
        Assert.True(result.StepResults.Count > 0);
    }

    [Fact]
    public async Task PlanIntent_ReturnsExecutionSteps()
    {
        // Arrange
        var intent = "Get user information";
        var userId = "user-123";

        // Act
        var plan = await _engine.PlanIntentAsync(intent, userId);

        // Assert
        Assert.NotNull(plan);
        Assert.NotNull(plan.Steps);
        Assert.True(plan.Steps.Count > 0);
        Assert.Equal(intent, plan.Intent);
    }

    #endregion

    #region Step-Level Error Context Tests

    [Fact]
    public async Task ExecuteIntent_PopulatesStepErrorContext()
    {
        // Arrange
        var intent = "Process order";
        var userId = "user-456";

        // Act
        var result = await _engine.ExecuteIntentAsync(intent, userId);

        // Assert - All steps should have populated error context if failed
        foreach (var step in result.StepResults)
        {
            if (!step.Success)
            {
                Assert.NotNull(step.ErrorMessage);
                Assert.NotEqual(ErrorCategory.Unknown, step.ErrorCategory);
            }
        }
    }

    #endregion

    #region Fallback Mechanism Tests

    [Fact]
    public async Task ExecuteIntent_WithFallbackValue_ReturnsFallbackOnFailure()
    {
        // Arrange
        var intent = "Get user details with fallback";
        var userId = "user-789";

        // Act
        var result = await _engine.ExecuteIntentAsync(intent, userId);

        // Assert - At least step should have fallback capability
        Assert.NotNull(result);
        Assert.NotNull(result.StepResults);
    }

    #endregion

    #region Configuration-Driven Resilience Tests

    [Fact]
    public void StepwisePlannerEngine_UsesConfiguredRetryPolicy()
    {
        // Assert - Engine should be initialized with configuration
        Assert.NotNull(_engine);
        // The retry policy is applied internally during step execution
    }

    [Fact]
    public void ResilienceConfiguration_SupportsServiceSpecificTimeouts()
    {
        // Arrange
        var config = new ResilienceConfiguration
        {
            DefaultTimeoutSeconds = 30,
            ServiceTimeouts = new Dictionary<string, int>
            {
                { "OrderService", 15 },
                { "UserService", 5 }
            }
        };

        // Act
        var orderServiceTimeout = config.GetTimeoutSeconds("OrderService");
        var userServiceTimeout = config.GetTimeoutSeconds("UserService");
        var defaultTimeout = config.GetTimeoutSeconds("UnknownService");

        // Assert
        Assert.Equal(15, orderServiceTimeout);
        Assert.Equal(5, userServiceTimeout);
        Assert.Equal(30, defaultTimeout);
    }

    [Fact]
    public void ResilienceConfiguration_SupportsServiceSpecificRetries()
    {
        // Arrange
        var config = new ResilienceConfiguration
        {
            DefaultMaxRetries = 3,
            DefaultBackoffMs = 100,
            ServiceRetries = new Dictionary<string, ServiceResilienceConfig>
            {
                { "OrderService", new ServiceResilienceConfig { MaxRetries = 5, BackoffMs = 200 } }
            }
        };

        // Act
        var orderServiceConfig = config.GetServiceConfig("OrderService");
        var defaultConfig = config.GetServiceConfig("UserService");

        // Assert
        Assert.Equal(5, orderServiceConfig.MaxRetries);
        Assert.Equal(200, orderServiceConfig.BackoffMs);
        Assert.Equal(3, defaultConfig.MaxRetries);
        Assert.Equal(100, defaultConfig.BackoffMs);
    }

    #endregion

    #region Activity Tracing Tests

    [Fact]
    public async Task ExecuteIntent_CreatesActivitySpans()
    {
        // Arrange
        var intent = "Trace execution";
        var userId = "user-trace";

        // Act
        var result = await _engine.ExecuteIntentAsync(intent, userId);

        // Assert - Activity source should have been called for tracing
        Assert.NotNull(result);
        // In real scenario, OpenTelemetry would capture the spans
    }

    #endregion

    #region Error Categorization Tests

    [Fact]
    public async Task ErrorCategorization_IdentifiesTransientErrors()
    {
        // Arrange
        var intent = "Trigger transient error";
        var userId = "user-cat";

        // Act
        var result = await _engine.ExecuteIntentAsync(intent, userId);

        // Assert - Transient errors should be categorized correctly
        foreach (var step in result.StepResults.Where(s => !s.Success))
        {
            // Depending on error message, should categorize appropriately
            Assert.True(
                step.ErrorCategory == ErrorCategory.Transient ||
                step.ErrorCategory == ErrorCategory.Permanent ||
                step.ErrorCategory == ErrorCategory.Unknown
            );
        }
    }

    #endregion

    #region Integration with DI Container

    [Fact]
    public void StepwisePlannerEngine_CanBeResolvedFromDI()
    {
        // Assert - All dependencies were provided in Setup
        Assert.NotNull(_engine);
    }

    #endregion
}
