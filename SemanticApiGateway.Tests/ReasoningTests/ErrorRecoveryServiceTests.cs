using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using SemanticApiGateway.Gateway.Features.ErrorHandling;
using SemanticApiGateway.Gateway.Models;

namespace SemanticApiGateway.Tests.ReasoningTests;

/// <summary>
/// Test suite for IErrorRecoveryService implementation.
/// Validates error analysis, recovery recommendations, and aggregation.
/// </summary>
public class ErrorRecoveryServiceTests
{
    private readonly Mock<ILogger<ErrorRecoveryService>> _mockLogger;
    private readonly IErrorRecoveryService _service;

    public ErrorRecoveryServiceTests()
    {
        _mockLogger = new Mock<ILogger<ErrorRecoveryService>>();
        _service = new ErrorRecoveryService(_mockLogger.Object);
    }

    #region GetRecoveryRecommendation Tests

    [Fact]
    public void GetRecoveryRecommendation_WithTransientError_ReturnsRetryAction()
    {
        // Arrange
        var errorReport = new ExecutionErrorReport
        {
            StepName = "GetOrder",
            ServiceName = "OrderService",
            FunctionName = "GetOrderById",
            Errors = new List<StepError>
            {
                new StepError
                {
                    Message = "Service temporarily unavailable",
                    Category = ErrorCategory.Transient,
                    RetryAttempts = 0,
                    HttpStatusCode = 503
                }
            },
            TotalDuration = TimeSpan.FromMilliseconds(100)
        };

        // Act
        var recommendation = _service.GetRecoveryRecommendation(errorReport, 1, 2);

        // Assert
        Assert.NotNull(recommendation);
        Assert.True(recommendation.IsRecoverable);
        Assert.True(recommendation.Action != RecoveryAction.None);
        Assert.Contains("Transient", recommendation.Reason);
    }

    [Fact]
    public void GetRecoveryRecommendation_WithPermanentError_ReturnsSkipOrAbort()
    {
        // Arrange
        var errorReport = new ExecutionErrorReport
        {
            StepName = "ValidateUser",
            ServiceName = "UserService",
            FunctionName = "GetUser",
            Errors = new List<StepError>
            {
                new StepError
                {
                    Message = "User not found",
                    Category = ErrorCategory.Permanent,
                    RetryAttempts = 0,
                    HttpStatusCode = 404
                }
            },
            TotalDuration = TimeSpan.FromMilliseconds(50)
        };

        // Act
        var recommendation = _service.GetRecoveryRecommendation(errorReport, 0, 2);

        // Assert
        Assert.NotNull(recommendation);
        Assert.Contains(RecoveryAction.SkipStepWithFallback, new[] { recommendation.Action, RecoveryAction.AbortExecution });
    }

    [Fact]
    public void GetRecoveryRecommendation_WithTimeout_ReturnsRetryWithLongerTimeout()
    {
        // Arrange
        var errorReport = new ExecutionErrorReport
        {
            StepName = "ProcessOrder",
            ServiceName = "OrderService",
            FunctionName = "ProcessOrder",
            Errors = new List<StepError>
            {
                new StepError
                {
                    Message = "Request timeout",
                    Category = ErrorCategory.Transient,
                    RetryAttempts = 2,
                    HttpStatusCode = 504
                }
            },
            TotalDuration = TimeSpan.FromSeconds(10)
        };

        // Act
        var recommendation = _service.GetRecoveryRecommendation(errorReport, 1, 2);

        // Assert
        Assert.NotNull(recommendation);
        Assert.Equal(RecoveryAction.RetryWithLongerTimeout, recommendation.Action);
    }

    [Fact]
    public void GetRecoveryRecommendation_WithMaxRetriesExceeded_SkipsWithFallback()
    {
        // Arrange
        var errorReport = new ExecutionErrorReport
        {
            StepName = "CreateOrder",
            ServiceName = "OrderService",
            FunctionName = "CreateOrder",
            Errors = new List<StepError>
            {
                new StepError
                {
                    Message = "Service overloaded",
                    Category = ErrorCategory.Transient,
                    RetryAttempts = 5,
                    HttpStatusCode = 503
                }
            },
            TotalDuration = TimeSpan.FromSeconds(15)
        };

        // Act
        var recommendation = _service.GetRecoveryRecommendation(errorReport, 2, 1);

        // Assert
        Assert.NotNull(recommendation);
        Assert.Equal(RecoveryAction.SkipStepWithFallback, recommendation.Action);
    }

    [Fact]
    public void GetRecoveryRecommendation_WithEmptyErrors_ReturnsNoRecovery()
    {
        // Arrange
        var errorReport = new ExecutionErrorReport
        {
            StepName = "Test",
            ServiceName = "TestService",
            FunctionName = "TestFunc",
            Errors = new List<StepError>(),
            TotalDuration = TimeSpan.Zero
        };

        // Act
        var recommendation = _service.GetRecoveryRecommendation(errorReport, 0, 0);

        // Assert
        Assert.NotNull(recommendation);
        Assert.False(recommendation.IsRecoverable);
        Assert.Equal(RecoveryAction.None, recommendation.Action);
    }

    #endregion

    #region CanRecover Tests

    [Fact]
    public void CanRecover_WithTransientError_ReturnsTrue()
    {
        // Arrange
        var errorReport = new ExecutionErrorReport
        {
            Errors = new List<StepError>
            {
                new StepError { Category = ErrorCategory.Transient }
            }
        };

        // Act
        var canRecover = _service.CanRecover(errorReport);

        // Assert
        Assert.True(canRecover);
    }

    [Fact]
    public void CanRecover_WithPermanentError_ReturnsFalse()
    {
        // Arrange
        var errorReport = new ExecutionErrorReport
        {
            Errors = new List<StepError>
            {
                new StepError { Category = ErrorCategory.Permanent }
            }
        };

        // Act
        var canRecover = _service.CanRecover(errorReport);

        // Assert
        Assert.False(canRecover);
    }

    [Fact]
    public void CanRecover_WithFallbackUsed_ReturnsTrue()
    {
        // Arrange
        var errorReport = new ExecutionErrorReport
        {
            Errors = new List<StepError>
            {
                new StepError
                {
                    Category = ErrorCategory.Permanent,
                    UsedFallback = true,
                    FallbackValue = "default"
                }
            }
        };

        // Act
        var canRecover = _service.CanRecover(errorReport);

        // Assert
        Assert.True(canRecover);
    }

    #endregion

    #region ShouldTripCircuitBreaker Tests

    [Fact]
    public void ShouldTripCircuitBreaker_WithThresholdExceeded_ReturnsTrue()
    {
        // Arrange
        var errors = new List<ExecutionErrorReport>
        {
            CreateErrorReport("ServiceA"),
            CreateErrorReport("ServiceA"),
            CreateErrorReport("ServiceA"),
            CreateErrorReport("ServiceA"),
            CreateErrorReport("ServiceA")
        };

        // Act
        var shouldTrip = _service.ShouldTripCircuitBreaker("ServiceA", errors, 5);

        // Assert
        Assert.True(shouldTrip);
    }

    [Fact]
    public void ShouldTripCircuitBreaker_BelowThreshold_ReturnsFalse()
    {
        // Arrange
        var errors = new List<ExecutionErrorReport>
        {
            CreateErrorReport("ServiceA"),
            CreateErrorReport("ServiceA"),
            CreateErrorReport("ServiceB")
        };

        // Act
        var shouldTrip = _service.ShouldTripCircuitBreaker("ServiceA", errors, 5);

        // Assert
        Assert.False(shouldTrip);
    }

    [Fact]
    public void ShouldTripCircuitBreaker_OutsideTimeWindow_ReturnsFalse()
    {
        // Arrange
        var pastErrors = new List<ExecutionErrorReport>
        {
            new ExecutionErrorReport
            {
                ServiceName = "ServiceA",
                Timestamp = DateTime.UtcNow.AddMinutes(-10)
            },
            new ExecutionErrorReport
            {
                ServiceName = "ServiceA",
                Timestamp = DateTime.UtcNow.AddMinutes(-10)
            }
        };

        // Act
        var shouldTrip = _service.ShouldTripCircuitBreaker("ServiceA", pastErrors, 2, TimeSpan.FromMinutes(5));

        // Assert
        Assert.False(shouldTrip);
    }

    [Fact]
    public void ShouldTripCircuitBreaker_DifferentService_ReturnsFalse()
    {
        // Arrange
        var errors = new List<ExecutionErrorReport>
        {
            CreateErrorReport("ServiceA"),
            CreateErrorReport("ServiceA"),
            CreateErrorReport("ServiceB")
        };

        // Act
        var shouldTrip = _service.ShouldTripCircuitBreaker("ServiceC", errors, 2);

        // Assert
        Assert.False(shouldTrip);
    }

    #endregion

    #region AggregateErrors Tests

    [Fact]
    public void AggregateErrors_WithMultipleErrors_CreatesComprehensiveReport()
    {
        // Arrange
        var errors = new List<ExecutionErrorReport>
        {
            new ExecutionErrorReport
            {
                StepName = "Step1",
                ServiceName = "ServiceA",
                Errors = new List<StepError>
                {
                    new StepError { Category = ErrorCategory.Transient, Message = "Timeout" }
                },
                TotalDuration = TimeSpan.FromSeconds(5),
                TotalRetryAttempts = 3
            },
            new ExecutionErrorReport
            {
                StepName = "Step2",
                ServiceName = "ServiceB",
                Errors = new List<StepError>
                {
                    new StepError { Category = ErrorCategory.Permanent, Message = "Not Found" }
                },
                TotalDuration = TimeSpan.FromSeconds(1),
                TotalRetryAttempts = 0
            }
        };

        // Act
        var aggregated = _service.AggregateErrors(errors, "test intent", "user123");

        // Assert
        Assert.NotNull(aggregated);
        Assert.Equal(2, aggregated.TotalStepErrors);
        Assert.Equal(3, aggregated.TotalRetryAttempts);
        Assert.True(aggregated.IsCritical); // Has permanent error
        Assert.NotEmpty(aggregated.CriticalFailures);
        Assert.Contains(ErrorCategory.Transient, aggregated.ErrorsByCategory.Keys);
        Assert.Contains(ErrorCategory.Permanent, aggregated.ErrorsByCategory.Keys);
    }

    [Fact]
    public void AggregateErrors_WithNullErrors_CreatesEmptyReport()
    {
        // Act
        var aggregated = _service.AggregateErrors(null, "test intent", "user123");

        // Assert
        Assert.NotNull(aggregated);
        Assert.Equal(0, aggregated.TotalStepErrors);
        Assert.False(aggregated.IsCritical);
    }

    [Fact]
    public void AggregateErrors_OnlyTransientErrors_NotCritical()
    {
        // Arrange
        var errors = new List<ExecutionErrorReport>
        {
            new ExecutionErrorReport
            {
                StepName = "Step1",
                ServiceName = "ServiceA",
                Errors = new List<StepError>
                {
                    new StepError { Category = ErrorCategory.Transient }
                }
            }
        };

        // Act
        var aggregated = _service.AggregateErrors(errors, "intent", "user");

        // Assert
        Assert.False(aggregated.IsCritical);
    }

    #endregion

    #region Helper Methods

    private ExecutionErrorReport CreateErrorReport(string serviceName)
    {
        return new ExecutionErrorReport
        {
            ServiceName = serviceName,
            Errors = new List<StepError>
            {
                new StepError { Category = ErrorCategory.Transient, Message = "Error" }
            },
            Timestamp = DateTime.UtcNow,
            TotalDuration = TimeSpan.FromMilliseconds(100)
        };
    }

    #endregion
}
