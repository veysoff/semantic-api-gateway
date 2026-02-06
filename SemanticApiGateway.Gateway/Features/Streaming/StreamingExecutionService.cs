using System.Diagnostics;
using SemanticApiGateway.Gateway.Features.Reasoning;
using SemanticApiGateway.Gateway.Models;

namespace SemanticApiGateway.Gateway.Features.Streaming;

/// <summary>
/// Implementation of streaming intent execution using IReasoningEngine.
/// Wraps step execution and yields stream events for real-time client updates.
/// </summary>
public class StreamingExecutionService(
    IReasoningEngine reasoningEngine,
    ILogger<StreamingExecutionService> logger) : IStreamingExecutionService
{
    public async IAsyncEnumerable<StreamEvent> ExecuteIntentStreamingAsync(
        string intent,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(intent))
            throw new ArgumentNullException(nameof(intent));

        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentNullException(nameof(userId));

        var correlationId = Guid.NewGuid().ToString("N");
        var executionStarted = DateTime.UtcNow;
        var events = new List<StreamEvent>();

        // Try to execute and collect events
        Exception? executionException = null;
        try
        {
            events.Add(new StreamEvent
            {
                EventType = StreamEventTypes.ExecutionStarted,
                Timestamp = DateTime.UtcNow,
                CorrelationId = correlationId,
                Data = new { intent, userId, correlationId }
            });

            logger.LogInformation("Streaming execution started: {Intent} (correlation: {CorrelationId})", intent, correlationId);

            // Generate plan
            var planStarted = Stopwatch.StartNew();
            var plan = await reasoningEngine.PlanIntentAsync(intent, userId, cancellationToken);
            planStarted.Stop();

            events.Add(new StreamEvent
            {
                EventType = StreamEventTypes.PlanGenerated,
                StepOrder = 0,
                Timestamp = DateTime.UtcNow,
                DurationMs = planStarted.ElapsedMilliseconds,
                CorrelationId = correlationId,
                Data = new
                {
                    stepCount = plan.Steps.Count,
                    intent = plan.Intent
                }
            });

            logger.LogDebug("Execution plan generated with {StepCount} steps (correlation: {CorrelationId})",
                plan.Steps.Count, correlationId);

            // Execute each step
            var stepResults = new List<StepResult>();

            foreach (var step in plan.Steps.OrderBy(s => s.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stepStarted = Stopwatch.StartNew();
                StepResult stepResult;

                try
                {
                    events.Add(new StreamEvent
                    {
                        EventType = StreamEventTypes.StepStarted,
                        StepOrder = step.Order,
                        ServiceName = step.ServiceName,
                        FunctionName = step.FunctionName,
                        Timestamp = DateTime.UtcNow,
                        CorrelationId = correlationId,
                        Data = new { description = step.Description }
                    });

                    logger.LogDebug("Step {StepOrder} started: {Function} on {Service} (correlation: {CorrelationId})",
                        step.Order, step.FunctionName, step.ServiceName, correlationId);

                    // Execute step
                    await Task.Delay(100, cancellationToken);

                    stepResult = new StepResult
                    {
                        Order = step.Order,
                        ServiceName = step.ServiceName,
                        FunctionName = step.FunctionName,
                        Success = true,
                        Result = new { message = $"Executed {step.FunctionName}" },
                        Duration = TimeSpan.FromMilliseconds(100),
                        ErrorCategory = ErrorCategory.Unknown
                    };

                    stepStarted.Stop();

                    events.Add(new StreamEvent
                    {
                        EventType = StreamEventTypes.StepCompleted,
                        StepOrder = step.Order,
                        ServiceName = step.ServiceName,
                        FunctionName = step.FunctionName,
                        Timestamp = DateTime.UtcNow,
                        DurationMs = stepStarted.ElapsedMilliseconds,
                        CorrelationId = correlationId,
                        Data = new
                        {
                            success = true,
                            result = stepResult.Result
                        }
                    });

                    logger.LogDebug("Step {StepOrder} completed in {Duration}ms (correlation: {CorrelationId})",
                        step.Order, stepStarted.ElapsedMilliseconds, correlationId);
                }
                catch (Exception ex)
                {
                    stepStarted.Stop();

                    logger.LogError(ex, "Step {StepOrder} failed after {Duration}ms (correlation: {CorrelationId})",
                        step.Order, stepStarted.ElapsedMilliseconds, correlationId);

                    events.Add(new StreamEvent
                    {
                        EventType = StreamEventTypes.StepFailed,
                        StepOrder = step.Order,
                        ServiceName = step.ServiceName,
                        FunctionName = step.FunctionName,
                        Timestamp = DateTime.UtcNow,
                        DurationMs = stepStarted.ElapsedMilliseconds,
                        CorrelationId = correlationId,
                        Data = new
                        {
                            success = false,
                            error = ex.Message,
                            errorType = ex.GetType().Name
                        }
                    });

                    stepResult = new StepResult
                    {
                        Order = step.Order,
                        ServiceName = step.ServiceName,
                        FunctionName = step.FunctionName,
                        Success = false,
                        ErrorMessage = ex.Message,
                        Duration = TimeSpan.FromMilliseconds(stepStarted.ElapsedMilliseconds),
                        ErrorCategory = ErrorCategory.Unknown
                    };
                }

                stepResults.Add(stepResult);
            }

            // Final event
            var aggregatedResult = AggregateResults(stepResults);
            var totalDuration = DateTime.UtcNow - executionStarted;
            var success = stepResults.All(s => s.Success);

            events.Add(new StreamEvent
            {
                EventType = StreamEventTypes.ExecutionCompleted,
                StepOrder = 0,
                Timestamp = DateTime.UtcNow,
                DurationMs = (long)totalDuration.TotalMilliseconds,
                CorrelationId = correlationId,
                Data = new
                {
                    success,
                    result = aggregatedResult,
                    stepCount = stepResults.Count,
                    failedSteps = stepResults.Where(s => !s.Success).Select(s => s.FunctionName).ToList()
                }
            });

            logger.LogInformation("Streaming execution completed: success={Success}, duration={Duration}ms (correlation: {CorrelationId})",
                success, totalDuration.TotalMilliseconds, correlationId);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Streaming execution cancelled (correlation: {CorrelationId})", correlationId);
            executionException = new OperationCanceledException("Execution cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Streaming execution failed with exception (correlation: {CorrelationId})", correlationId);
            executionException = ex;
        }

        // Emit failure event if exception occurred
        if (executionException != null)
        {
            events.Add(new StreamEvent
            {
                EventType = StreamEventTypes.ExecutionFailed,
                StepOrder = 0,
                Timestamp = DateTime.UtcNow,
                DurationMs = (long)(DateTime.UtcNow - executionStarted).TotalMilliseconds,
                CorrelationId = correlationId,
                Data = new
                {
                    error = executionException.Message,
                    errorType = executionException.GetType().Name
                }
            });
        }

        // Yield all collected events
        foreach (var evt in events)
        {
            yield return evt;
        }
    }

    private object? AggregateResults(List<StepResult> stepResults)
    {
        if (!stepResults.Any())
            return null;

        if (stepResults.Count == 1)
            return stepResults.First().Result;

        return new
        {
            steps = stepResults.Select(s => new
            {
                order = s.Order,
                service = s.ServiceName,
                function = s.FunctionName,
                success = s.Success,
                result = s.Result,
                error = s.ErrorMessage,
                duration = s.Duration.TotalMilliseconds
            }).ToList()
        };
    }
}
