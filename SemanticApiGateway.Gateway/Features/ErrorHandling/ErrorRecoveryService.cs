using SemanticApiGateway.Gateway.Models;
using System.Diagnostics;

namespace SemanticApiGateway.Gateway.Features.ErrorHandling;

/// <summary>
/// Advanced error recovery service providing intelligent recovery strategies.
/// Analyzes error patterns, suggests recovery actions, and enables partial execution recovery.
/// </summary>
public interface IErrorRecoveryService
{
    /// <summary>
    /// Generates recovery recommendation for a failed step.
    /// Analyzes error category, retry history, and service state.
    /// </summary>
    RecoveryRecommendation GetRecoveryRecommendation(
        ExecutionErrorReport errorReport,
        int totalExecutedSteps,
        int remainingSteps);

    /// <summary>
    /// Determines if a failed step should attempt recovery.
    /// Based on error type, retry count, and configured recovery strategies.
    /// </summary>
    bool CanRecover(ExecutionErrorReport errorReport);

    /// <summary>
    /// Analyzes error patterns to determine if circuit breaker should trip.
    /// Returns true if error rate exceeds threshold within time window.
    /// </summary>
    bool ShouldTripCircuitBreaker(
        string serviceName,
        IReadOnlyList<ExecutionErrorReport> recentErrors,
        int errorThreshold = 5,
        TimeSpan? timeWindow = null);

    /// <summary>
    /// Aggregates multiple step errors into a comprehensive report.
    /// Used when orchestration has multiple failures.
    /// </summary>
    AggregatedErrorReport AggregateErrors(
        IEnumerable<ExecutionErrorReport> stepErrors,
        string intent,
        string userId);
}

/// <summary>
/// Production implementation of error recovery service.
/// </summary>
public class ErrorRecoveryService(ILogger<ErrorRecoveryService> logger) : IErrorRecoveryService
{
    private const int DefaultErrorThreshold = 5;
    private static readonly TimeSpan DefaultTimeWindow = TimeSpan.FromMinutes(5);

    public RecoveryRecommendation GetRecoveryRecommendation(
        ExecutionErrorReport errorReport,
        int totalExecutedSteps,
        int remainingSteps)
    {
        if (errorReport?.Errors == null || errorReport.Errors.Count == 0)
        {
            return new RecoveryRecommendation
            {
                Action = RecoveryAction.None,
                Reason = "No errors to recover from",
                IsRecoverable = false
            };
        }

        var lastError = errorReport.Errors.Last();
        var recommendation = new RecoveryRecommendation
        {
            ServiceName = errorReport.ServiceName,
            FunctionName = errorReport.FunctionName,
            ErrorMessage = lastError.Message,
            ErrorCategory = lastError.Category,
            RetryAttempts = lastError.RetryAttempts,
            TotalDuration = errorReport.TotalDuration
        };

        // Determine recovery action based on error category and context
        recommendation.Action = DetermineRecoveryAction(
            lastError.Category,
            lastError.RetryAttempts,
            totalExecutedSteps,
            remainingSteps,
            lastError.HttpStatusCode);

        recommendation.IsRecoverable = recommendation.Action != RecoveryAction.None;
        recommendation.Reason = GenerateRecoveryReason(recommendation.Action, lastError);

        // Provide actionable suggestions
        recommendation.Suggestions = GenerateSuggestions(
            recommendation.Action,
            errorReport.ServiceName,
            lastError.Category);

        logger.LogInformation(
            "Recovery recommendation generated for {Service}.{Function}: Action={Action}, Category={Category}",
            errorReport.ServiceName,
            errorReport.FunctionName,
            recommendation.Action,
            lastError.Category);

        return recommendation;
    }

    public bool CanRecover(ExecutionErrorReport errorReport)
    {
        if (errorReport?.Errors == null || errorReport.Errors.Count == 0)
            return false;

        var lastError = errorReport.Errors.Last();

        // Transient errors can always recover with retry
        if (lastError.Category == ErrorCategory.Transient)
            return true;

        // If we already have a fallback value, recovery was done
        if (lastError.UsedFallback)
            return true;

        // Permanent errors cannot recover
        return false;
    }

    public bool ShouldTripCircuitBreaker(
        string serviceName,
        IReadOnlyList<ExecutionErrorReport> recentErrors,
        int errorThreshold = DefaultErrorThreshold,
        TimeSpan? timeWindow = null)
    {
        if (string.IsNullOrEmpty(serviceName) || recentErrors.Count == 0)
            return false;

        timeWindow ??= DefaultTimeWindow;
        var cutoffTime = DateTime.UtcNow - timeWindow.Value;

        // Count errors from this service within time window
        var relevantErrors = recentErrors
            .Where(e => e.ServiceName == serviceName && e.Timestamp > cutoffTime)
            .ToList();

        var shouldTrip = relevantErrors.Count >= errorThreshold;

        if (shouldTrip)
        {
            logger.LogWarning(
                "Circuit breaker should trip for {Service}: {ErrorCount} errors in {TimeWindow}",
                serviceName,
                relevantErrors.Count,
                timeWindow);
        }

        return shouldTrip;
    }

    public AggregatedErrorReport AggregateErrors(
        IEnumerable<ExecutionErrorReport> stepErrors,
        string intent,
        string userId)
    {
        var errorsList = stepErrors?.ToList() ?? [];

        var report = new AggregatedErrorReport
        {
            Intent = intent,
            UserId = userId,
            TotalStepErrors = errorsList.Count,
            Timestamp = DateTime.UtcNow,
            StepErrors = errorsList
        };

        if (errorsList.Count > 0)
        {
            // Categorize errors by type
            var byCategory = errorsList
                .SelectMany(e => e.Errors)
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            report.ErrorsByCategory = byCategory;

            // Calculate statistics
            report.TotalRetryAttempts = errorsList.Sum(e => e.TotalRetryAttempts);
            report.TotalErrorDuration = TimeSpan.FromMilliseconds(
                errorsList.Sum(e => e.TotalDuration.TotalMilliseconds));

            // Identify critical path failures
            var criticalErrors = errorsList
                .Where(e => e.Errors.Any(err => err.Category == ErrorCategory.Permanent))
                .ToList();

            report.CriticalFailures = criticalErrors;
            report.IsCritical = criticalErrors.Count > 0;

            // Generate overall recommendation
            report.OverallRecommendation = GenerateAggregateRecommendation(errorsList);
        }

        logger.LogInformation(
            "Aggregated {ErrorCount} step errors for intent from user {UserId}: {Intent}",
            report.TotalStepErrors,
            userId,
            intent);

        return report;
    }

    private RecoveryAction DetermineRecoveryAction(
        ErrorCategory category,
        int retryAttempts,
        int totalExecutedSteps,
        int remainingSteps,
        int? httpStatusCode)
    {
        // Circuit breaker open - skip step
        if (httpStatusCode == 503 && retryAttempts > 0)
            return RecoveryAction.SkipStepWithFallback;

        // Timeout - could retry with longer timeout
        if (httpStatusCode == 504 || httpStatusCode == 408)
            return RecoveryAction.RetryWithLongerTimeout;

        // Transient errors - retry
        if (category == ErrorCategory.Transient)
            return retryAttempts >= 3
                ? RecoveryAction.SkipStepWithFallback
                : RecoveryAction.RetryImmediate;

        // Permanent errors - skip with fallback or abort
        if (category == ErrorCategory.Permanent)
            return remainingSteps == 0
                ? RecoveryAction.AbortExecution
                : RecoveryAction.SkipStepWithFallback;

        // Unknown - conservative approach
        return retryAttempts > 1
            ? RecoveryAction.SkipStepWithFallback
            : RecoveryAction.RetryWithBackoff;
    }

    private string GenerateRecoveryReason(RecoveryAction action, StepError error)
    {
        return action switch
        {
            RecoveryAction.RetryImmediate =>
                $"Transient error detected. Retrying immediately. Error: {error.Message}",

            RecoveryAction.RetryWithBackoff =>
                $"Transient error detected. Retrying with exponential backoff. Error: {error.Message}",

            RecoveryAction.RetryWithLongerTimeout =>
                $"Timeout error detected. Retrying with increased timeout. Error: {error.Message}",

            RecoveryAction.SkipStepWithFallback =>
                $"Permanent error or max retries exceeded. Using fallback value. Error: {error.Message}",

            RecoveryAction.AbortExecution =>
                $"Critical permanent error and no fallback available. Aborting execution. Error: {error.Message}",

            RecoveryAction.CircuitBreakerOpen =>
                $"Circuit breaker is open for this service. Stopping further attempts.",

            _ => "No recovery action needed."
        };
    }

    private List<string> GenerateSuggestions(
        RecoveryAction action,
        string? serviceName,
        ErrorCategory category)
    {
        var suggestions = new List<string>();

        if (action == RecoveryAction.SkipStepWithFallback)
        {
            suggestions.Add("Consider defining a fallback value for this step if not already present.");
            suggestions.Add("Review the service health and logs for underlying issues.");
        }

        if (action == RecoveryAction.CircuitBreakerOpen)
        {
            suggestions.Add($"Service '{serviceName}' may be experiencing issues. Check service health.");
            suggestions.Add("Consider increasing timeouts or retry counts if service is slow.");
        }

        if (category == ErrorCategory.Permanent)
        {
            suggestions.Add("This is a permanent error. Check request parameters and authorization.");
            suggestions.Add("Verify the service API is compatible with the request.");
        }

        if (action == RecoveryAction.RetryWithLongerTimeout)
        {
            suggestions.Add("The service is slow. Consider increasing timeout configuration.");
            suggestions.Add("Check if the service has resource constraints.");
        }

        return suggestions;
    }

    private string GenerateAggregateRecommendation(List<ExecutionErrorReport> errors)
    {
        var permanentCount = errors
            .SelectMany(e => e.Errors)
            .Count(e => e.Category == ErrorCategory.Permanent);

        var transientCount = errors
            .SelectMany(e => e.Errors)
            .Count(e => e.Category == ErrorCategory.Transient);

        if (permanentCount > 0)
        {
            return $"{permanentCount} permanent errors found. Consider reviewing request parameters and service compatibility.";
        }

        if (transientCount > errors.Count / 2)
        {
            return "Multiple transient errors detected. Service may be experiencing issues. Retry after delay.";
        }

        return "Review individual step errors for detailed recovery recommendations.";
    }
}

/// <summary>
/// Enumeration of recovery actions that can be taken for a failed step.
/// </summary>
public enum RecoveryAction
{
    None = 0,
    RetryImmediate = 1,
    RetryWithBackoff = 2,
    RetryWithLongerTimeout = 3,
    SkipStepWithFallback = 4,
    AbortExecution = 5,
    CircuitBreakerOpen = 6
}

/// <summary>
/// Detailed recommendation for recovering from a step failure.
/// </summary>
public class RecoveryRecommendation
{
    public RecoveryAction Action { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsRecoverable { get; set; }
    public string? ServiceName { get; set; }
    public string? FunctionName { get; set; }
    public string? ErrorMessage { get; set; }
    public ErrorCategory? ErrorCategory { get; set; }
    public int RetryAttempts { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public List<string> Suggestions { get; set; } = [];
}

/// <summary>
/// Aggregated report of multiple step errors from an execution.
/// </summary>
public class AggregatedErrorReport
{
    public string Intent { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int TotalStepErrors { get; set; }
    public TimeSpan TotalErrorDuration { get; set; }
    public int TotalRetryAttempts { get; set; }
    public bool IsCritical { get; set; }
    public List<ExecutionErrorReport> StepErrors { get; set; } = [];
    public List<ExecutionErrorReport> CriticalFailures { get; set; } = [];
    public Dictionary<ErrorCategory, int> ErrorsByCategory { get; set; } = [];
    public string OverallRecommendation { get; set; } = string.Empty;
}
