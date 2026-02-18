using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using SemanticApiGateway.Gateway.Features.ErrorHandling;

namespace SemanticApiGateway.Tests.ReasoningTests;

/// <summary>
/// Test suite for ICircuitBreakerService implementation.
/// Validates circuit breaker metrics and policy creation.
/// </summary>
public class CircuitBreakerServiceTests
{
    private readonly Mock<ILogger<CircuitBreakerService>> _mockLogger;
    private readonly ICircuitBreakerService _service;

    public CircuitBreakerServiceTests()
    {
        _mockLogger = new Mock<ILogger<CircuitBreakerService>>();
        _service = new CircuitBreakerService(_mockLogger.Object);
    }

    #region Policy Creation Tests

    [Fact]
    public void GetCircuitBreakerPolicy_ReturnsPolicyForGenericType()
    {
        // Act
        var policy = _service.GetCircuitBreakerPolicy<string>("ServiceA");

        // Assert
        Assert.NotNull(policy);
    }

    [Fact]
    public void GetCircuitBreakerPolicy_MultipleCalls_CreatesPerService()
    {
        // Act
        var policyA1 = _service.GetCircuitBreakerPolicy<string>("ServiceA");
        var policyA2 = _service.GetCircuitBreakerPolicy<string>("ServiceA");
        var policyB = _service.GetCircuitBreakerPolicy<string>("ServiceB");

        // Assert
        Assert.NotNull(policyA1);
        Assert.NotNull(policyA2);
        Assert.NotNull(policyB);
    }

    #endregion

    #region Circuit State Tests

    [Fact]
    public void GetCircuitState_NewService_ReturnsClosed()
    {
        // Act
        var state = _service.GetCircuitState("NewService");

        // Assert
        Assert.Equal(CircuitState.Closed, state);
    }

    [Fact]
    public void GetCircuitState_WithValidServiceName_ReturnsClosedInitially()
    {
        // Arrange
        _service.GetCircuitBreakerPolicy<string>("ServiceA");

        // Act
        var state = _service.GetCircuitState("ServiceA");

        // Assert
        Assert.Equal(CircuitState.Closed, state);
    }

    [Fact]
    public void ResetCircuitBreaker_ValidService_Succeeds()
    {
        // Arrange
        _service.GetCircuitBreakerPolicy<string>("ServiceA");

        // Act - Should not throw
        _service.ResetCircuitBreaker("ServiceA");

        // Assert
        var state = _service.GetCircuitState("ServiceA");
        Assert.Equal(CircuitState.Closed, state);
    }

    #endregion

    #region Metrics Tests

    [Fact]
    public void GetMetrics_ReturnsMetricsWithServiceName()
    {
        // Act
        var metrics = _service.GetMetrics("ServiceA");

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal("ServiceA", metrics.ServiceName);
    }

    [Fact]
    public void GetMetrics_NewService_HasClosedState()
    {
        // Act
        var metrics = _service.GetMetrics("ServiceA");

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(CircuitState.Closed, metrics.State);
        Assert.True(metrics.IsClosed);
        Assert.False(metrics.IsOpen);
        Assert.False(metrics.IsHalfOpen);
    }

    [Fact]
    public void GetMetrics_ReturnsZeroCountsInitially()
    {
        // Act
        var metrics = _service.GetMetrics("ServiceA");

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(0, metrics.FailureCount);
        Assert.Equal(0, metrics.SuccessCount);
    }

    #endregion

    #region Recording Tests

    [Fact]
    public void RecordFailure_WithValidService_DoesNotThrow()
    {
        // Act & Assert - Should not throw
        _service.RecordFailure("ServiceA");
        _service.RecordFailure("ServiceB");
    }

    [Fact]
    public void RecordSuccess_WithValidService_DoesNotThrow()
    {
        // Act & Assert - Should not throw
        _service.RecordSuccess("ServiceA");
        _service.RecordSuccess("ServiceB");
    }

    [Fact]
    public void RecordFailure_WithNullServiceName_HandlesSafely()
    {
        // Act & Assert - Should not throw
        _service.RecordFailure(null!);
        _service.RecordFailure("");
    }

    [Fact]
    public void RecordSuccess_WithNullServiceName_HandlesSafely()
    {
        // Act & Assert - Should not throw
        _service.RecordSuccess(null!);
        _service.RecordSuccess("");
    }

    #endregion

    #region Safety Tests

    [Fact]
    public void GetCircuitState_WithNullService_ReturnsClosed()
    {
        // Act
        var state = _service.GetCircuitState(null!);

        // Assert
        Assert.Equal(CircuitState.Closed, state);
    }

    [Fact]
    public void GetMetrics_WithNullService_ReturnsDefaultMetrics()
    {
        // Act
        var metrics = _service.GetMetrics(null!);

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(CircuitState.Closed, metrics.State);
    }

    [Fact]
    public void ResetCircuitBreaker_WithNullService_DoesNotThrow()
    {
        // Act & Assert - Should not throw
        _service.ResetCircuitBreaker(null!);
        _service.ResetCircuitBreaker("");
    }

    #endregion

    #region Isolation Tests

    [Fact]
    public void MultipleServices_MaintainIndependentMetrics()
    {
        // Act
        var metricsA = _service.GetMetrics("ServiceA");
        var metricsB = _service.GetMetrics("ServiceB");

        // Assert - Different services should be independent
        Assert.NotEqual(metricsA.ServiceName, metricsB.ServiceName);
        Assert.Equal("ServiceA", metricsA.ServiceName);
        Assert.Equal("ServiceB", metricsB.ServiceName);
    }

    #endregion

    #region Policy Type Tests

    [Fact]
    public void GetCircuitBreakerPolicy_SupportsMultipleGenericTypes()
    {
        // Act
        var policyString = _service.GetCircuitBreakerPolicy<string>("ServiceA");
        var policyObject = _service.GetCircuitBreakerPolicy<object>("ServiceA");
        var policyDto = _service.GetCircuitBreakerPolicy<CircuitBreakerMetrics>("ServiceA");

        // Assert
        Assert.NotNull(policyString);
        Assert.NotNull(policyObject);
        Assert.NotNull(policyDto);
    }

    #endregion
}
