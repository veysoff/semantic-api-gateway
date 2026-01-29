using Microsoft.AspNetCore.Authentication.JwtBearer;
using SemanticApiGateway.Gateway.Features.Reasoning;
using SemanticApiGateway.Gateway.Features.Security;
using SemanticApiGateway.Gateway.Models;

namespace SemanticApiGateway.Gateway.Endpoints;

/// <summary>
/// Minimal API endpoints for natural language intent execution and planning
/// </summary>
public static class IntentEndpoints
{
    public static IEndpointRouteBuilder MapIntentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/intent")
            .RequireAuthorization(JwtBearerDefaults.AuthenticationScheme)
            .WithTags("Intent");

        group.MapPost("/execute", ExecuteIntent)
            .WithName("ExecuteIntent")
            .Produces<ExecuteIntentResponse>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(429)
            .Produces<ErrorResponse>(500);

        group.MapPost("/plan", PlanIntent)
            .WithName("PlanIntent")
            .Produces<PlanIntentResponse>(200)
            .Produces<ErrorResponse>(400)
            .Produces<ErrorResponse>(401)
            .Produces<ErrorResponse>(500);

        return endpoints;
    }

    /// <summary>
    /// Execute a natural language intent
    /// </summary>
    private static async Task<IResult> ExecuteIntent(
        ExecuteIntentRequest request,
        IReasoningEngine reasoningEngine,
        ITokenPropagationService tokenPropagationService,
        ISemanticGuardrailService guardrailService,
        ILogger<Program> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Intent))
        {
            return Results.BadRequest(new ErrorResponse { Error = "Intent cannot be empty" });
        }

        try
        {
            // Extract user ID from token
            var token = tokenPropagationService.ExtractToken(httpContext);
            if (string.IsNullOrEmpty(token))
            {
                return Results.Unauthorized();
            }

            var userId = tokenPropagationService.GetUserIdFromToken(token);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            logger.LogInformation("Intent execution requested by user {UserId}: {Intent}", userId, request.Intent);

            // Validate intent against guardrails
            var validationResult = await guardrailService.ValidateIntentAsync(request.Intent, userId);
            if (!validationResult.IsAllowed)
            {
                logger.LogWarning("Intent blocked by guardrails for user {UserId}: {Reason}",
                    userId, validationResult.ReasonDenied);

                return validationResult.ValidationType switch
                {
                    GuardrailValidationType.RateLimitExceeded => Results.StatusCode(429),
                    GuardrailValidationType.PromptInjectionDetected => Results.BadRequest(new ErrorResponse
                    {
                        Error = "Potential security threat detected",
                        Details = validationResult.ReasonDenied
                    }),
                    GuardrailValidationType.UnauthorizedOperation => Results.Unauthorized(),
                    _ => Results.BadRequest(new ErrorResponse
                    {
                        Error = "Intent validation failed",
                        Details = validationResult.ReasonDenied
                    })
                };
            }

            // Execute the intent
            var result = await reasoningEngine.ExecuteIntentAsync(request.Intent, userId, cancellationToken);

            // Record execution for audit
            await guardrailService.RecordIntentExecutionAsync(
                userId,
                request.Intent,
                result.Success,
                result.AggregatedResult?.ToString());

            if (!result.Success)
            {
                logger.LogError("Intent execution failed for user {UserId}: {Error}",
                    userId, result.ErrorMessage);
                return Results.InternalServerError();
            }

            logger.LogInformation("Intent executed successfully for user {UserId}", userId);

            return Results.Ok(new ExecuteIntentResponse
            {
                Success = true,
                Result = result.AggregatedResult,
                ExecutionTimeMs = (long)result.ExecutionTime.TotalMilliseconds,
                ExecutedAt = result.ExecutedAt,
                PlanId = result.PlanId
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error executing intent");
            return Results.InternalServerError();
        }
    }

    /// <summary>
    /// Generate an execution plan without executing it
    /// </summary>
    private static async Task<IResult> PlanIntent(
        ExecuteIntentRequest request,
        IReasoningEngine reasoningEngine,
        ITokenPropagationService tokenPropagationService,
        ILogger<Program> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Intent))
        {
            return Results.BadRequest(new ErrorResponse { Error = "Intent cannot be empty" });
        }

        try
        {
            // Extract user ID from token
            var token = tokenPropagationService.ExtractToken(httpContext);
            if (string.IsNullOrEmpty(token))
            {
                return Results.Unauthorized();
            }

            var userId = tokenPropagationService.GetUserIdFromToken(token);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            logger.LogInformation("Plan generation requested by user {UserId} for intent: {Intent}",
                userId, request.Intent);

            // Generate plan
            var plan = await reasoningEngine.PlanIntentAsync(request.Intent, userId, cancellationToken);

            return Results.Ok(new PlanIntentResponse
            {
                PlanId = plan.Id,
                Intent = plan.Intent,
                Steps = plan.Steps.Select(s => new StepDto
                {
                    Order = s.Order,
                    ServiceName = s.ServiceName,
                    FunctionName = s.FunctionName,
                    Description = s.Description,
                    Parameters = s.Parameters
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error generating plan");
            return Results.InternalServerError();
        }
    }
}
