using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using SemanticApiGateway.Gateway.Features.Reasoning;
using SemanticApiGateway.Gateway.Features.Streaming;
using SemanticApiGateway.Gateway.Models;

namespace SemanticApiGateway.Tests.ReasoningTests;

public class StreamingExecutionServiceTests
{
    private readonly Mock<IReasoningEngine> _mockEngine;
    private readonly Mock<ILogger<StreamingExecutionService>> _mockLogger;
    private readonly StreamingExecutionService _service;

    public StreamingExecutionServiceTests()
    {
        _mockEngine = new Mock<IReasoningEngine>();
        _mockLogger = new Mock<ILogger<StreamingExecutionService>>();
        _service = new StreamingExecutionService(_mockEngine.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ExecuteIntentStreaming_EmitsExecutionStartedEvent()
    {
        // Arrange
        var intent = "Get user data";
        var userId = "user-123";
        var plan = CreateMockPlan(1);

        _mockEngine.Setup(e => e.PlanIntentAsync(intent, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        // Act
        var events = new List<StreamEvent>();
        await foreach (var evt in _service.ExecuteIntentStreamingAsync(intent, userId))
        {
            events.Add(evt);
        }

        // Assert
        Assert.NotEmpty(events);
        var startEvent = events.First();
        Assert.Equal(StreamEventTypes.ExecutionStarted, startEvent.EventType);
        Assert.NotNull(startEvent.CorrelationId);
    }

    [Fact]
    public async Task ExecuteIntentStreaming_EmitsPlanGeneratedEvent()
    {
        // Arrange
        var intent = "Get user data";
        var userId = "user-123";
        var plan = CreateMockPlan(3);

        _mockEngine.Setup(e => e.PlanIntentAsync(intent, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        // Act
        var events = new List<StreamEvent>();
        await foreach (var evt in _service.ExecuteIntentStreamingAsync(intent, userId))
        {
            events.Add(evt);
        }

        // Assert
        var planEvent = events.FirstOrDefault(e => e.EventType == StreamEventTypes.PlanGenerated);
        Assert.NotNull(planEvent);
        Assert.Equal(0, planEvent.StepOrder);
    }

    [Fact]
    public async Task ExecuteIntentStreaming_EmitsStepEvents()
    {
        // Arrange
        var intent = "Get user data";
        var userId = "user-123";
        var plan = CreateMockPlan(2);

        _mockEngine.Setup(e => e.PlanIntentAsync(intent, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        // Act
        var events = new List<StreamEvent>();
        await foreach (var evt in _service.ExecuteIntentStreamingAsync(intent, userId))
        {
            events.Add(evt);
        }

        // Assert - Should have step_started and step_completed for each step
        var stepStartedEvents = events.Where(e => e.EventType == StreamEventTypes.StepStarted).ToList();
        var stepCompletedEvents = events.Where(e => e.EventType == StreamEventTypes.StepCompleted).ToList();

        Assert.Equal(2, stepStartedEvents.Count);
        Assert.Equal(2, stepCompletedEvents.Count);
    }

    [Fact]
    public async Task ExecuteIntentStreaming_StepEventsHaveCorrectOrder()
    {
        // Arrange
        var intent = "Multi-step intent";
        var userId = "user-123";
        var plan = CreateMockPlan(3);

        _mockEngine.Setup(e => e.PlanIntentAsync(intent, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        // Act
        var events = new List<StreamEvent>();
        await foreach (var evt in _service.ExecuteIntentStreamingAsync(intent, userId))
        {
            events.Add(evt);
        }

        // Assert
        var stepStartedEvents = events.Where(e => e.EventType == StreamEventTypes.StepStarted).ToList();
        for (int i = 0; i < stepStartedEvents.Count; i++)
        {
            Assert.Equal(i + 1, stepStartedEvents[i].StepOrder);
        }
    }

    [Fact]
    public async Task ExecuteIntentStreaming_EmitsExecutionCompletedEvent()
    {
        // Arrange
        var intent = "Get user data";
        var userId = "user-123";
        var plan = CreateMockPlan(1);

        _mockEngine.Setup(e => e.PlanIntentAsync(intent, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        // Act
        var events = new List<StreamEvent>();
        await foreach (var evt in _service.ExecuteIntentStreamingAsync(intent, userId))
        {
            events.Add(evt);
        }

        // Assert
        var finalEvent = events.Last();
        Assert.Equal(StreamEventTypes.ExecutionCompleted, finalEvent.EventType);
        Assert.NotNull(finalEvent.Data);
    }

    [Fact]
    public async Task ExecuteIntentStreaming_AllEventsHaveSameCorrelationId()
    {
        // Arrange
        var intent = "Get user data";
        var userId = "user-123";
        var plan = CreateMockPlan(2);

        _mockEngine.Setup(e => e.PlanIntentAsync(intent, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        // Act
        var events = new List<StreamEvent>();
        await foreach (var evt in _service.ExecuteIntentStreamingAsync(intent, userId))
        {
            events.Add(evt);
        }

        // Assert
        var correlationIds = events.Select(e => e.CorrelationId).Distinct().ToList();
        Assert.Single(correlationIds);
        Assert.NotNull(correlationIds[0]);
    }

    [Fact]
    public async Task ExecuteIntentStreaming_StepCompletedHasDuration()
    {
        // Arrange
        var intent = "Get user data";
        var userId = "user-123";
        var plan = CreateMockPlan(1);

        _mockEngine.Setup(e => e.PlanIntentAsync(intent, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        // Act
        var events = new List<StreamEvent>();
        await foreach (var evt in _service.ExecuteIntentStreamingAsync(intent, userId))
        {
            events.Add(evt);
        }

        // Assert
        var completedEvent = events.FirstOrDefault(e => e.EventType == StreamEventTypes.StepCompleted);
        Assert.NotNull(completedEvent);
        Assert.True(completedEvent.DurationMs > 0);
    }

    [Fact]
    public async Task ExecuteIntentStreaming_NullIntent_ThrowsArgumentNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in _service.ExecuteIntentStreamingAsync(null!, "user-123"))
            {
                // Never executes
            }
        });
    }

    [Fact]
    public async Task ExecuteIntentStreaming_NullUserId_ThrowsArgumentNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in _service.ExecuteIntentStreamingAsync("intent", null!))
            {
                // Never executes
            }
        });
    }

    [Fact]
    public async Task ExecuteIntentStreaming_EngineException_EmitsFailureEvent()
    {
        // Arrange
        var intent = "Get user data";
        var userId = "user-123";

        _mockEngine.Setup(e => e.PlanIntentAsync(intent, userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Plan generation failed"));

        // Act
        var events = new List<StreamEvent>();
        await foreach (var evt in _service.ExecuteIntentStreamingAsync(intent, userId))
        {
            events.Add(evt);
        }

        // Assert
        var failureEvent = events.FirstOrDefault(e => e.EventType == StreamEventTypes.ExecutionFailed);
        Assert.NotNull(failureEvent);
        Assert.Contains("Plan generation failed", failureEvent.Data?.ToString() ?? "");
    }

    [Fact]
    public async Task ExecuteIntentStreaming_EventsHaveTimestamps()
    {
        // Arrange
        var intent = "Get user data";
        var userId = "user-123";
        var plan = CreateMockPlan(1);

        _mockEngine.Setup(e => e.PlanIntentAsync(intent, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan);

        // Act
        var events = new List<StreamEvent>();
        await foreach (var evt in _service.ExecuteIntentStreamingAsync(intent, userId))
        {
            events.Add(evt);
        }

        // Assert
        Assert.All(events, e => Assert.NotEqual(default(DateTime), e.Timestamp));
    }

    private ExecutionPlan CreateMockPlan(int stepCount)
    {
        var steps = Enumerable.Range(1, stepCount)
            .Select(i => new ExecutionStep
            {
                Order = i,
                ServiceName = $"Service{i}",
                FunctionName = $"Function{i}",
                Description = $"Step {i}",
                Parameters = new Dictionary<string, object> { { "param", $"value{i}" } }
            })
            .ToList();

        return new ExecutionPlan
        {
            Intent = "Test intent",
            Steps = steps
        };
    }
}
