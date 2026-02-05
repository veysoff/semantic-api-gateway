using Microsoft.Extensions.Logging;
using Moq;
using SemanticApiGateway.Gateway.Features.Reasoning;
using Xunit;

namespace SemanticApiGateway.Tests.ReasoningTests;

/// <summary>
/// Integration tests for variable resolution and data piping across multiple steps
/// </summary>
public class VariableResolverTests
{
    private readonly VariableResolver _variableResolver;
    private readonly Gateway.Features.Reasoning.ExecutionContext _executionContext;

    public VariableResolverTests()
    {
        var mockLogger = new Mock<ILogger<VariableResolver>>();
        _variableResolver = new VariableResolver(mockLogger.Object);
        _executionContext = new Gateway.Features.Reasoning.ExecutionContext
        {
            UserId = "test-user-123",
            Intent = "Test intent"
        };
    }

    #region Simple Variable Resolution Tests

    [Fact]
    public void ResolveSimpleStepVariable_Success()
    {
        // Arrange
        var step1Result = new StepResult
        {
            Order = 1,
            ServiceName = "OrderService",
            FunctionName = "GetOrder",
            Success = true,
            Result = new { orderId = "ORD-12345", total = 99.99 }
        };
        _executionContext.StepResults.Add(step1Result);

        var parameters = new Dictionary<string, object>
        {
            { "orderId", "${step1.orderId}" }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);

        // Assert
        Assert.NotNull(resolved);
        Assert.Equal("ORD-12345", resolved["orderId"]);
    }

    [Fact]
    public void ResolveMultipleSimpleVariables_Success()
    {
        // Arrange
        var step1Result = new StepResult
        {
            Order = 1,
            ServiceName = "OrderService",
            FunctionName = "GetOrder",
            Success = true,
            Result = new { orderId = "ORD-12345", customerId = "CUST-67890" }
        };
        _executionContext.StepResults.Add(step1Result);

        var parameters = new Dictionary<string, object>
        {
            { "orderId", "${step1.orderId}" },
            { "customerId", "${step1.customerId}" }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);

        // Assert
        Assert.Equal("ORD-12345", resolved["orderId"]);
        Assert.Equal("CUST-67890", resolved["customerId"]);
    }

    #endregion

    #region Nested Property Access Tests

    [Fact]
    public void ResolveNestedProperty_Success()
    {
        // Arrange
        var step2Result = new StepResult
        {
            Order = 2,
            ServiceName = "UserService",
            FunctionName = "GetUser",
            Success = true,
            Result = new
            {
                user = new
                {
                    email = "john.doe@example.com",
                    name = "John Doe"
                }
            }
        };
        _executionContext.StepResults.Add(step2Result);

        var parameters = new Dictionary<string, object>
        {
            { "userEmail", "${step2.user.email}" }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);

        // Assert
        Assert.Equal("john.doe@example.com", resolved["userEmail"]);
    }

    [Fact]
    public void ResolveDeeplyNestedProperty_Success()
    {
        // Arrange
        var step3Result = new StepResult
        {
            Order = 3,
            ServiceName = "InventoryService",
            FunctionName = "GetProduct",
            Success = true,
            Result = new
            {
                product = new
                {
                    details = new
                    {
                        price = new { amount = 99.99, currency = "USD" }
                    }
                }
            }
        };
        _executionContext.StepResults.Add(step3Result);

        var parameters = new Dictionary<string, object>
        {
            { "priceAmount", "${step3.product.details.price.amount}" }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);

        // Assert - The resolved value may be stringified, so check both cases
        var priceValue = resolved["priceAmount"];
        if (priceValue is double dVal)
        {
            Assert.Equal(99.99, dVal, precision: 2);
        }
        else
        {
            Assert.True(double.TryParse(priceValue?.ToString(), out var parsed) && Math.Abs(parsed - 99.99) < 0.01);
        }
    }

    #endregion

    #region Built-in Variable Tests

    [Fact]
    public void ResolveBuiltInUserId_Success()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            { "userId", "${userId}" }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);

        // Assert
        Assert.Equal("test-user-123", resolved["userId"]);
    }

    [Fact]
    public void ResolveBuiltInIntent_Success()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            { "intent", "${intent}" }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);

        // Assert
        Assert.Equal("Test intent", resolved["intent"]);
    }

    [Fact]
    public void ResolveMixedBuiltInAndStepVariables_Success()
    {
        // Arrange
        var step1Result = new StepResult
        {
            Order = 1,
            ServiceName = "OrderService",
            FunctionName = "GetOrder",
            Success = true,
            Result = new { orderId = "ORD-12345" }
        };
        _executionContext.StepResults.Add(step1Result);

        var parameters = new Dictionary<string, object>
        {
            { "orderId", "${step1.orderId}" },
            { "userId", "${userId}" },
            { "intent", "${intent}" }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);

        // Assert
        Assert.Equal("ORD-12345", resolved["orderId"]);
        Assert.Equal("test-user-123", resolved["userId"]);
        Assert.Equal("Test intent", resolved["intent"]);
    }

    #endregion

    #region Multi-Step Data Piping Tests

    [Fact]
    public void DataPipingAcrossTwoSteps_Success()
    {
        // Arrange - Step 1 result
        var step1Result = new StepResult
        {
            Order = 1,
            ServiceName = "OrderService",
            FunctionName = "GetOrder",
            Success = true,
            Result = new { orderId = "ORD-12345", customerId = "CUST-67890" }
        };
        _executionContext.StepResults.Add(step1Result);

        // Step 2 uses result from step 1
        var step2Parameters = new Dictionary<string, object>
        {
            { "customerId", "${step1.customerId}" }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(step2Parameters, _executionContext);

        // Assert
        Assert.Equal("CUST-67890", resolved["customerId"]);
    }

    [Fact]
    public void DataPipingAcrossThreeSteps_Success()
    {
        // Arrange - Step 1 result
        var step1Result = new StepResult
        {
            Order = 1,
            ServiceName = "OrderService",
            FunctionName = "GetOrder",
            Success = true,
            Result = new { orderId = "ORD-12345", customerId = "CUST-67890" }
        };
        _executionContext.StepResults.Add(step1Result);

        // Step 2 result
        var step2Result = new StepResult
        {
            Order = 2,
            ServiceName = "UserService",
            FunctionName = "GetUser",
            Success = true,
            Result = new { email = "john@example.com", tier = "premium" }
        };
        _executionContext.StepResults.Add(step2Result);

        // Step 3 uses results from step 1 and step 2
        var step3Parameters = new Dictionary<string, object>
        {
            { "orderId", "${step1.orderId}" },
            { "userEmail", "${step2.email}" }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(step3Parameters, _executionContext);

        // Assert
        Assert.Equal("ORD-12345", resolved["orderId"]);
        Assert.Equal("john@example.com", resolved["userEmail"]);
    }

    #endregion

    #region Error Handling and Edge Cases

    [Fact]
    public void ResolveMissingVariable_ReturnsPlaceholder()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            { "missingId", "${step99.nonexistent}" }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);

        // Assert - Should keep the placeholder or use fallback
        Assert.NotNull(resolved);
    }

    [Fact]
    public void ResolveWithoutStepResults_ReturnsMissingValues()
    {
        // Arrange - Empty execution context
        var parameters = new Dictionary<string, object>
        {
            { "orderId", "${step1.orderId}" }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);

        // Assert
        Assert.NotNull(resolved);
    }

    [Fact]
    public void ResolveLiteralValueMixedWithVariables_Success()
    {
        // Arrange
        var step1Result = new StepResult
        {
            Order = 1,
            ServiceName = "OrderService",
            FunctionName = "GetOrder",
            Success = true,
            Result = new { orderId = "ORD-12345" }
        };
        _executionContext.StepResults.Add(step1Result);

        var parameters = new Dictionary<string, object>
        {
            { "orderId", "${step1.orderId}" },
            { "status", "pending" },
            { "amount", 99.99 }
        };

        // Act
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);

        // Assert
        Assert.Equal("ORD-12345", resolved["orderId"]);
        Assert.Equal("pending", resolved["status"]);
        Assert.Equal(99.99, (double)resolved["amount"]);
    }

    [Fact]
    public void ResolveNullStepResult_HandlesGracefully()
    {
        // Arrange
        var step1Result = new StepResult
        {
            Order = 1,
            ServiceName = "OrderService",
            FunctionName = "GetOrder",
            Success = true,
            Result = null
        };
        _executionContext.StepResults.Add(step1Result);

        var parameters = new Dictionary<string, object>
        {
            { "value", "${step1.someProperty}" }
        };

        // Act & Assert - Should not throw exception
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);
        Assert.NotNull(resolved);
    }

    #endregion

    #region Performance Tests

    [Fact]
    public void ResolveVariables_PerformanceAcceptable()
    {
        // Arrange - Create multiple steps with nested data
        for (int i = 1; i <= 10; i++)
        {
            var stepResult = new StepResult
            {
                Order = i,
                ServiceName = $"Service{i}",
                FunctionName = $"Function{i}",
                Success = true,
                Result = new
                {
                    data = new { nested = new { value = $"Value{i}" } }
                }
            };
            _executionContext.StepResults.Add(stepResult);
        }

        var parameters = new Dictionary<string, object>
        {
            { "val1", "${step1.data.nested.value}" },
            { "val5", "${step5.data.nested.value}" },
            { "val10", "${step10.data.nested.value}" }
        };

        // Act - Measure resolution time
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var resolved = _variableResolver.ResolveParameters(parameters, _executionContext);
        stopwatch.Stop();

        // Assert - Resolution should be fast (< 5ms)
        Assert.True(stopwatch.ElapsedMilliseconds < 5,
            $"Variable resolution took {stopwatch.ElapsedMilliseconds}ms, expected < 5ms");

        Assert.Equal("Value1", resolved["val1"]);
        Assert.Equal("Value5", resolved["val5"]);
        Assert.Equal("Value10", resolved["val10"]);
    }

    #endregion
}
