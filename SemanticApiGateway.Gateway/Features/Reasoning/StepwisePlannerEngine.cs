using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Polly;
using SemanticApiGateway.Gateway.Configuration;
using SemanticApiGateway.Gateway.Features.Observability;
using SemanticApiGateway.Gateway.Models;

namespace SemanticApiGateway.Gateway.Features.Reasoning;

/// <summary>
/// Orchestrates multi-step execution plans using Semantic Kernel's stepwise planner
/// Pipes data between microservice calls and aggregates results with resilience patterns
/// Includes distributed tracing with OpenTelemetry for observability
/// </summary>
public class StepwisePlannerEngine : IReasoningEngine
{
    private readonly Kernel _kernel;
    private readonly ILogger<StepwisePlannerEngine> _logger;
    private readonly VariableResolver _variableResolver;
    private readonly IAsyncPolicy<ExecutionResult> _executionPolicy;
    private readonly ResilienceConfiguration _resilienceConfig;
    private readonly IGatewayActivitySource _activitySource;

    public StepwisePlannerEngine(
        Kernel kernel,
        ILogger<StepwisePlannerEngine> logger,
        VariableResolver variableResolver,
        IOptions<ResilienceConfiguration> resilienceOptions,
        IGatewayActivitySource activitySource)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _variableResolver = variableResolver ?? throw new ArgumentNullException(nameof(variableResolver));
        _resilienceConfig = resilienceOptions?.Value ?? throw new ArgumentNullException(nameof(resilienceOptions));
        _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));

        // Configure resilience policy for the overall execution
        _executionPolicy = Policy<ExecutionResult>
            .Handle<Exception>()
            .OrResult(r => !r.Success)
            .FallbackAsync(fallbackAction => Task.FromResult(new ExecutionResult
            {
                Success = false,
                ErrorMessage = "Execution failed after retries"
            }));
    }

    public async Task<ExecutionResult> ExecuteIntentAsync(
        string intent,
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(intent))
            throw new ArgumentNullException(nameof(intent));

        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentNullException(nameof(userId));

        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("Executing intent from user {UserId}: {Intent}", userId, intent);

            // Generate execution plan
            var plan = await PlanIntentAsync(intent, userId, cancellationToken);
            var executionContext = new ExecutionContext
            {
                UserId = userId,
                Intent = intent,
                StepResults = new List<StepResult>()
            };

            // Execute steps sequentially with data piping
            var stepResults = await ExecuteStepsAsync(plan.Steps, executionContext, cancellationToken);

            // Aggregate results
            var aggregatedResult = AggregateResults(stepResults);

            var result = new ExecutionResult
            {
                PlanId = plan.Id,
                Intent = intent,
                Success = stepResults.All(s => s.Success),
                AggregatedResult = aggregatedResult,
                StepResults = stepResults,
                ExecutionTime = DateTime.UtcNow - startTime,
            };

            if (result.Success)
            {
                _logger.LogInformation("Successfully executed intent {Intent} in {Duration}ms",
                    intent, result.ExecutionTime.TotalMilliseconds);
            }
            else
            {
                var failedSteps = stepResults.Where(s => !s.Success).Select(s => s.FunctionName);
                _logger.LogWarning("Intent {Intent} completed with failures in steps: {Steps}",
                    intent, string.Join(", ", failedSteps));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical failure executing intent: {Intent}", intent);
            return new ExecutionResult
            {
                Intent = intent,
                Success = false,
                ErrorMessage = $"Critical execution error: {ex.Message}",
                ExecutionTime = DateTime.UtcNow - startTime,
            };
        }
    }

    /// <summary>
    /// Executes multiple steps sequentially with data piping and error handling
    /// Each step creates a child activity span for distributed tracing
    /// </summary>
    private async Task<List<StepResult>> ExecuteStepsAsync(
        List<ExecutionStep> steps,
        ExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var results = new List<StepResult>();

        foreach (var step in steps.OrderBy(s => s.Order))
        {
            var stepStartTime = DateTime.UtcNow;

            // Create activity span for this step
            using var stepActivity = _activitySource.StartStepExecutionSpan(step.Order, step.ServiceName, step.FunctionName);

            try
            {
                // Resolve parameters using previous step results
                using var resolveActivity = _activitySource.StartVariableResolutionSpan($"Step{step.Order}Parameters");
                var resolveStartTime = DateTime.UtcNow;

                var resolvedParameters = _variableResolver.ResolveParameters(step.Parameters, executionContext);
                var resolveDuration = (long)(DateTime.UtcNow - resolveStartTime).TotalMilliseconds;

                _activitySource.RecordVariableMetrics(
                    resolveActivity,
                    success: true,
                    durationMs: resolveDuration,
                    resolvedValue: "parameters"
                );

                var stepResult = await ExecuteStepAsync(step, resolvedParameters, cancellationToken);
                results.Add(stepResult);

                // Record step metrics to activity
                var stepDuration = (long)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;
                _activitySource.RecordStepMetrics(
                    stepActivity,
                    success: stepResult.Success,
                    durationMs: stepDuration,
                    retryCount: stepResult.RetryCount,
                    errorMessage: stepResult.ErrorMessage
                );

                // Add to context for next step
                executionContext.StepResults.Add(stepResult);

                if (!stepResult.Success)
                {
                    _logger.LogWarning("Step {Order} ({Function}) failed: {Error} | Retries: {RetryCount} | Category: {ErrorCategory} | Duration: {DurationMs}ms",
                        step.Order, step.FunctionName, stepResult.ErrorMessage, stepResult.RetryCount,
                        stepResult.ErrorCategory, stepDuration);
                }
                else
                {
                    _logger.LogInformation("Step {Order} ({Function}) completed in {DurationMs}ms",
                        step.Order, step.FunctionName, stepDuration);
                }
            }
            catch (Exception ex)
            {
                var stepDuration = (long)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;

                _logger.LogError(ex, "Unexpected error executing step {Order} ({Function}) after {DurationMs}ms",
                    step.Order, step.FunctionName, stepDuration);

                // Record error metrics to activity
                _activitySource.RecordStepMetrics(
                    stepActivity,
                    success: false,
                    durationMs: stepDuration,
                    retryCount: 0,
                    errorMessage: ex.Message
                );

                results.Add(new StepResult
                {
                    Order = step.Order,
                    ServiceName = step.ServiceName,
                    FunctionName = step.FunctionName,
                    Success = false,
                    ErrorMessage = $"Execution error: {ex.Message}",
                    ErrorCategory = ErrorCategory.Unknown,
                    Duration = TimeSpan.FromMilliseconds(stepDuration)
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Executes a single step with retry policy, timeout, and fallback handling
    /// </summary>
    private async Task<StepResult> ExecuteStepAsync(
        ExecutionStep step,
        Dictionary<string, object> resolvedParameters,
        CancellationToken cancellationToken)
    {
        var stepStartTime = DateTime.UtcNow;
        var retryPolicy = CreateRetryPolicy(step.ServiceName);

        try
        {
            var result = await retryPolicy.ExecuteAsync(async () =>
            {
                // TODO: Implement actual microservice invocation
                // For now, return placeholder success
                await Task.Delay(100, cancellationToken);

                return new StepResult
                {
                    Order = step.Order,
                    ServiceName = step.ServiceName,
                    FunctionName = step.FunctionName,
                    Success = true,
                    Result = new { message = $"Placeholder result from {step.FunctionName}" },
                    Duration = DateTime.UtcNow - stepStartTime,
                    ErrorCategory = ErrorCategory.Unknown
                };
            });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step {Order} failed after retries: {Function} on {Service}",
                step.Order, step.FunctionName, step.ServiceName);

            // Check if we should use fallback value
            if (step.FallbackValue != null)
            {
                _logger.LogInformation("Using fallback value for step {Order} ({Function})",
                    step.Order, step.FunctionName);

                return new StepResult
                {
                    Order = step.Order,
                    ServiceName = step.ServiceName,
                    FunctionName = step.FunctionName,
                    Success = true,
                    Result = step.FallbackValue,
                    ErrorMessage = ex.Message,
                    Duration = DateTime.UtcNow - stepStartTime,
                    UsedFallback = true,
                    ErrorCategory = CategorizeError(ex.Message),
                    Error = CreateStepError(ex, 0, null)
                };
            }

            return new StepResult
            {
                Order = step.Order,
                ServiceName = step.ServiceName,
                FunctionName = step.FunctionName,
                Success = false,
                ErrorMessage = ex.Message,
                Duration = DateTime.UtcNow - stepStartTime,
                ErrorCategory = CategorizeError(ex.Message),
                Error = CreateStepError(ex, 0, null)
            };
        }
    }

    /// <summary>
    /// Creates a retry policy with exponential backoff for a service
    /// Configuration-driven with service-specific overrides
    /// </summary>
    private IAsyncPolicy<StepResult> CreateRetryPolicy(string serviceName)
    {
        // Get service-specific configuration
        var serviceConfig = _resilienceConfig.GetServiceConfig(serviceName);
        var timeoutMs = _resilienceConfig.GetTimeoutMs(serviceName);

        var retryPolicy = Policy<StepResult>
            .Handle<Exception>()
            .OrResult(r => !r.Success && ShouldRetry(r))
            .WaitAndRetryAsync(
                retryCount: serviceConfig.MaxRetries,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * serviceConfig.BackoffMs), // Exponential backoff
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    if (outcome.Exception != null)
                    {
                        _logger.LogWarning("Retry {Attempt}/{MaxRetries} for {Service} after {Delay}ms due to: {Exception}",
                            retryCount, serviceConfig.MaxRetries, serviceName,
                            timespan.TotalMilliseconds, outcome.Exception.Message);
                    }
                    else
                    {
                        _logger.LogWarning("Retry {Attempt}/{MaxRetries} for {Service} after {Delay}ms (failure)",
                            retryCount, serviceConfig.MaxRetries, serviceName, timespan.TotalMilliseconds);

                        // Track retry attempt in result
                        if (outcome.Result != null)
                        {
                            outcome.Result.RetryCount = retryCount;
                            outcome.Result.TotalRetryDuration += timespan;
                            outcome.Result.RetryReasons.Add(outcome.Result.ErrorMessage ?? "Unknown error");
                        }
                    }
                });

        // Wrap with timeout policy
        var timeoutPolicy = Policy.TimeoutAsync<StepResult>(
            TimeSpan.FromMilliseconds(timeoutMs),
            Polly.Timeout.TimeoutStrategy.Optimistic);

        return timeoutPolicy.WrapAsync(retryPolicy);
    }

    /// <summary>
    /// Determines if a step should be retried based on error type
    /// </summary>
    private bool ShouldRetry(StepResult result)
    {
        if (result.Success)
            return false;

        // Retry on transient errors (timeouts, service unavailable, etc.)
        var transientErrors = new[] { "timeout", "unavailable", "connection", "transient" };
        var errorLower = result.ErrorMessage?.ToLowerInvariant() ?? string.Empty;

        return transientErrors.Any(e => errorLower.Contains(e));
    }

    /// <summary>
    /// Categorizes an error as Transient, Permanent, or Unknown
    /// </summary>
    private ErrorCategory CategorizeError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return ErrorCategory.Unknown;

        var errorLower = errorMessage.ToLowerInvariant();

        // Transient errors that are worth retrying
        var transientKeywords = new[] { "timeout", "unavailable", "connection", "transient", "503", "504", "429", "temporary" };
        if (transientKeywords.Any(e => errorLower.Contains(e)))
            return ErrorCategory.Transient;

        // Permanent errors that won't succeed on retry
        var permanentKeywords = new[] { "unauthorized", "forbidden", "notfound", "invalid", "400", "401", "403", "404" };
        if (permanentKeywords.Any(e => errorLower.Contains(e)))
            return ErrorCategory.Permanent;

        return ErrorCategory.Unknown;
    }

    /// <summary>
    /// Creates a StepError from an exception with optional retry history
    /// </summary>
    private StepError CreateStepError(Exception ex, int retryCount, List<RetryAttempt>? retryHistory)
    {
        return new StepError
        {
            Message = ex.Message,
            Category = CategorizeError(ex.Message),
            RetryAttempts = retryCount,
            TotalRetryDuration = TimeSpan.Zero,
            RetryHistory = retryHistory ?? new List<RetryAttempt>(),
            StackTrace = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" ? ex.StackTrace : null
        };
    }

    /// <summary>
    /// Aggregates results from all steps into a single result
    /// </summary>
    private object? AggregateResults(List<StepResult> stepResults)
    {
        if (!stepResults.Any())
            return null;

        if (stepResults.Count == 1)
            return stepResults.First().Result;

        // Return aggregated results as a dictionary
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

    public async Task<ExecutionPlan> PlanIntentAsync(string intent, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(intent))
            throw new ArgumentNullException(nameof(intent));

        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentNullException(nameof(userId));

        try
        {
            _logger.LogInformation("Creating execution plan for intent: {Intent}", intent);

            // TODO: Implement plan generation
            // Use Semantic Kernel to understand the intent and map to microservice functions

            var plan = new ExecutionPlan
            {
                Intent = intent,
                Steps = new List<ExecutionStep>
                {
                    // Placeholder step
                    new ExecutionStep
                    {
                        Order = 1,
                        ServiceName = "UserService",
                        FunctionName = "GetUser",
                        Description = "Retrieve user information",
                        Parameters = new Dictionary<string, object>
                        {
                            { "userId", userId }
                        }
                    }
                }
            };

            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create execution plan for intent: {Intent}", intent);
            throw;
        }
    }
}
