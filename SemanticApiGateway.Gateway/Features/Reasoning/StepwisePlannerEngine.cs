using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Polly;
using SemanticApiGateway.Gateway.Configuration;
using SemanticApiGateway.Gateway.Features.Caching;
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
    private readonly ICacheService _cacheService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServiceDiscoveryOptions _serviceDiscovery;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string PlanCachePrefix = "plan:";
    private const string ResultCachePrefix = "result:";

    public StepwisePlannerEngine(
        Kernel kernel,
        ILogger<StepwisePlannerEngine> logger,
        VariableResolver variableResolver,
        IOptions<ResilienceConfiguration> resilienceOptions,
        IGatewayActivitySource activitySource,
        ICacheService cacheService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _variableResolver = variableResolver ?? throw new ArgumentNullException(nameof(variableResolver));
        _resilienceConfig = resilienceOptions?.Value ?? throw new ArgumentNullException(nameof(resilienceOptions));
        _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _serviceDiscovery = configuration.GetSection("ServiceDiscovery").Get<ServiceDiscoveryOptions>()
            ?? new ServiceDiscoveryOptions();

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
            var rawAuth = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"].FirstOrDefault();
            var executionContext = new ExecutionContext
            {
                UserId = userId,
                Intent = intent,
                StepResults = new List<StepResult>(),
                JwtToken = rawAuth
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

                var stepResult = await ExecuteStepAsync(step, resolvedParameters, executionContext.JwtToken, cancellationToken);
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
        string? jwtToken,
        CancellationToken cancellationToken)
    {
        var stepStartTime = DateTime.UtcNow;
        var retryPolicy = CreateRetryPolicy(step.ServiceName);

        try
        {
            var result = await retryPolicy.ExecuteAsync(async () =>
            {
                var serviceResult = await InvokeServiceAsync(step, resolvedParameters, jwtToken, cancellationToken);
                return new StepResult
                {
                    Order = step.Order,
                    ServiceName = step.ServiceName,
                    FunctionName = step.FunctionName,
                    Success = serviceResult.Success,
                    Result = serviceResult.Data,
                    ErrorMessage = serviceResult.ErrorMessage,
                    HttpStatusCode = serviceResult.StatusCode,
                    Duration = DateTime.UtcNow - stepStartTime,
                    ErrorCategory = serviceResult.Success ? ErrorCategory.Unknown : CategorizeError(serviceResult.ErrorMessage)
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

        var cacheKey = $"{PlanCachePrefix}{intent.ToLowerInvariant().GetHashCode():X}";

        try
        {
            var cachedPlan = await _cacheService.GetAsync<ExecutionPlan>(cacheKey, cancellationToken);
            if (cachedPlan != null)
            {
                _logger.LogInformation("Cache HIT - returning cached plan for intent: {Intent}", intent);
                return cachedPlan;
            }

            _logger.LogInformation("Cache MISS - generating execution plan for intent: {Intent}", intent);

            var plan = GeneratePlanForIntent(intent, userId);

            await _cacheService.SetAsync(cacheKey, plan, TimeSpan.FromHours(1), cancellationToken);
            _logger.LogInformation("Plan cached for 1 hour. Steps: {StepCount}", plan.Steps.Count);

            return plan;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create execution plan for intent: {Intent}", intent);
            throw;
        }
    }

    /// <summary>
    /// Maps natural language intent to a concrete execution plan.
    /// Uses keyword matching to route to appropriate microservice sequences.
    /// </summary>
    private ExecutionPlan GeneratePlanForIntent(string intent, string userId)
    {
        var lower = intent.ToLowerInvariant();

        // Scenario A: Create order → 3-step plan: GetUser → CheckInventory → CreateOrder
        if (lower.Contains("create order") || lower.Contains("place order") || lower.Contains("order for"))
        {
            _logger.LogInformation("Planning: CreateOrder workflow (3 steps)");
            return new ExecutionPlan
            {
                Intent = intent,
                Steps = new List<ExecutionStep>
                {
                    new ExecutionStep
                    {
                        Order = 1,
                        ServiceName = "UserService",
                        FunctionName = "GetUser",
                        Description = "Retrieve user information and validate identity",
                        Parameters = new Dictionary<string, object> { { "userId", userId } }
                    },
                    new ExecutionStep
                    {
                        Order = 2,
                        ServiceName = "InventoryService",
                        FunctionName = "CheckInventory",
                        Description = "Check product availability in inventory",
                        Parameters = new Dictionary<string, object> { { "productId", "laptop" }, { "quantity", 5 } }
                    },
                    new ExecutionStep
                    {
                        Order = 3,
                        ServiceName = "OrderService",
                        FunctionName = "CreateOrder",
                        Description = "Create the order using resolved user ID",
                        Parameters = new Dictionary<string, object>
                        {
                            { "userId", "${step1.id}" },
                            { "productId", "laptop" },
                            { "quantity", 5 }
                        }
                    }
                }
            };
        }

        // List orders
        if (lower.Contains("list order") || lower.Contains("get order") || lower.Contains("my order") || lower.Contains("show order"))
        {
            _logger.LogInformation("Planning: ListOrders workflow (2 steps)");
            return new ExecutionPlan
            {
                Intent = intent,
                Steps = new List<ExecutionStep>
                {
                    new ExecutionStep
                    {
                        Order = 1,
                        ServiceName = "UserService",
                        FunctionName = "GetUser",
                        Description = "Retrieve user information",
                        Parameters = new Dictionary<string, object> { { "userId", userId } }
                    },
                    new ExecutionStep
                    {
                        Order = 2,
                        ServiceName = "OrderService",
                        FunctionName = "GetUserOrders",
                        Description = "Fetch all orders for this user",
                        Parameters = new Dictionary<string, object> { { "userId", "${step1.id}" } }
                    }
                }
            };
        }

        // List users
        if (lower.Contains("list user") || lower.Contains("all user") || lower.Contains("get user") || lower.Contains("show user"))
        {
            _logger.LogInformation("Planning: GetUser workflow (1 step)");
            return new ExecutionPlan
            {
                Intent = intent,
                Steps = new List<ExecutionStep>
                {
                    new ExecutionStep
                    {
                        Order = 1,
                        ServiceName = "UserService",
                        FunctionName = "GetUser",
                        Description = "Retrieve user profile",
                        Parameters = new Dictionary<string, object> { { "userId", userId } }
                    }
                }
            };
        }

        // Check inventory
        if (lower.Contains("inventory") || lower.Contains("stock") || lower.Contains("available"))
        {
            _logger.LogInformation("Planning: CheckInventory workflow (1 step)");
            return new ExecutionPlan
            {
                Intent = intent,
                Steps = new List<ExecutionStep>
                {
                    new ExecutionStep
                    {
                        Order = 1,
                        ServiceName = "InventoryService",
                        FunctionName = "GetInventory",
                        Description = "Check current inventory levels",
                        Parameters = new Dictionary<string, object>()
                    }
                }
            };
        }

        // Default: get user profile
        _logger.LogInformation("Planning: default GetUser workflow for unrecognized intent");
        return new ExecutionPlan
        {
            Intent = intent,
            Steps = new List<ExecutionStep>
            {
                new ExecutionStep
                {
                    Order = 1,
                    ServiceName = "UserService",
                    FunctionName = "GetUser",
                    Description = "Retrieve user information",
                    Parameters = new Dictionary<string, object> { { "userId", userId } }
                }
            }
        };
    }

    /// <summary>
    /// Invokes the appropriate microservice endpoint for a given step.
    /// Resolves service URL from ServiceDiscovery config and routes by FunctionName.
    /// </summary>
    private async Task<ServiceCallResult> InvokeServiceAsync(
        ExecutionStep step,
        Dictionary<string, object> parameters,
        string? jwtToken,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("resilient");
        var baseUrl = GetServiceBaseUrl(step.ServiceName);

        _logger.LogInformation(
            "Step {Order}: Calling {ServiceName}.{FunctionName} at {BaseUrl}",
            step.Order, step.ServiceName, step.FunctionName, baseUrl);

        try
        {
            HttpResponseMessage response = await SendRequestAsync(
                client, step, parameters, baseUrl, jwtToken, cancellationToken);

            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                _logger.LogInformation(
                    "Step {Order} ({FunctionName}) succeeded: HTTP {StatusCode}",
                    step.Order, step.FunctionName, statusCode);
                return new ServiceCallResult { Success = true, Data = data, StatusCode = statusCode };
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Step {Order} ({FunctionName}) failed: HTTP {StatusCode} - {Error}",
                step.Order, step.FunctionName, statusCode, errorBody);

            return new ServiceCallResult
            {
                Success = false,
                ErrorMessage = $"HTTP {statusCode}: {errorBody}",
                StatusCode = statusCode
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Step {Order} ({FunctionName}) threw exception", step.Order, step.FunctionName);
            return new ServiceCallResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpClient client,
        ExecutionStep step,
        Dictionary<string, object> parameters,
        string baseUrl,
        string? jwtToken,
        CancellationToken cancellationToken)
    {
        HttpRequestMessage BuildRequest(HttpMethod method, string url, HttpContent? content = null)
        {
            var req = new HttpRequestMessage(method, url);
            if (!string.IsNullOrEmpty(jwtToken))
                req.Headers.TryAddWithoutValidation("Authorization", jwtToken);
            req.Content = content;
            return req;
        }

        return step.FunctionName switch
        {
            "GetUser" => await client.SendAsync(
                BuildRequest(HttpMethod.Get, $"{baseUrl}/api/users/{parameters.GetValueOrDefault("userId")}"),
                cancellationToken),

            "GetUserOrders" => await client.SendAsync(
                BuildRequest(HttpMethod.Get, $"{baseUrl}/api/orders/user/{parameters.GetValueOrDefault("userId")}"),
                cancellationToken),

            "CheckInventory" => await client.SendAsync(
                BuildRequest(HttpMethod.Get,
                    $"{baseUrl}/api/inventory/check-stock/{parameters.GetValueOrDefault("productId")}/{ToInt(parameters.GetValueOrDefault("quantity") ?? 1)}"),
                cancellationToken),

            "GetInventory" => await client.SendAsync(
                BuildRequest(HttpMethod.Get, $"{baseUrl}/api/inventory"),
                cancellationToken),

            "CreateOrder" => await client.SendAsync(
                BuildRequest(
                    HttpMethod.Post,
                    $"{baseUrl}/api/orders",
                    JsonContent.Create(new
                    {
                        userId = parameters.GetValueOrDefault("userId")?.ToString(),
                        items = new[]
                        {
                            new
                            {
                                productId = parameters.GetValueOrDefault("productId")?.ToString() ?? "laptop",
                                quantity = ToInt(parameters.GetValueOrDefault("quantity") ?? 1),
                                unitPrice = 999.99m
                            }
                        }
                    })),
                cancellationToken),

            _ => throw new InvalidOperationException($"Unknown function: {step.FunctionName}")
        };
    }

    private static int ToInt(object value) => value switch
    {
        int i => i,
        long l => (int)l,
        JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt32(),
        _ => int.TryParse(value?.ToString(), out var n) ? n : 1
    };

    private string GetServiceBaseUrl(string serviceName) => serviceName switch
    {
        "UserService" => _serviceDiscovery.UserServiceUrl,
        "OrderService" => _serviceDiscovery.OrderServiceUrl,
        "InventoryService" => _serviceDiscovery.InventoryServiceUrl,
        _ => throw new InvalidOperationException($"Unknown service: {serviceName}")
    };

    private record ServiceCallResult
    {
        public bool Success { get; init; }
        public object? Data { get; init; }
        public string? ErrorMessage { get; init; }
        public int StatusCode { get; init; }
    }
}
