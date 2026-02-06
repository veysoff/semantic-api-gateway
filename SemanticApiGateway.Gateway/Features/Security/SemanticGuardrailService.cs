using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace SemanticApiGateway.Gateway.Features.Security;

/// <summary>
/// Implements semantic guardrails for prompt injection detection, RBAC, and rate limiting
/// </summary>
public class SemanticGuardrailService : ISemanticGuardrailService
{
    private readonly ILogger<SemanticGuardrailService> _logger;
    private readonly ConcurrentDictionary<string, (int count, DateTime resetTime)> _rateLimitTracking;

    // Patterns for common prompt injection techniques
    private static readonly string[] InjectionPatterns = new[]
    {
        @"(?i)(ignore|forget|override|bypass)\s+(previous|prior|instruction|prompt|rule|constraint)",
        @"(?i)(as\s+an|acting\s+as|pretend|role\s+play)",
        @"(?i)(sql|xss|script|eval|exec|system|rm\s+-rf)",
        @"(?i)\{\{.*\}\}|\[\[.*\]\]",  // Template injection
        @"(?i)(<!--.*?-->|<script|javascript:|onerror=)",  // HTML/JS injection
    };

    private static readonly string[] RestrictedOperations = new[]
    {
        "delete", "drop", "truncate", "format", "wipe", "destroy"
    };

    private static readonly string[] RestrictedFunctions = new[]
    {
        "admin", "system", "internal", "config", "setting"
    };

    public SemanticGuardrailService(ILogger<SemanticGuardrailService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimitTracking = new ConcurrentDictionary<string, (int, DateTime)>();
    }

    public async Task<GuardrailValidationResult> ValidateIntentAsync(string intent, string userId)
    {
        var result = new GuardrailValidationResult { IsAllowed = true, ValidationType = GuardrailValidationType.Valid };

        if (string.IsNullOrWhiteSpace(intent))
        {
            result.IsAllowed = false;
            result.ReasonDenied = "Intent cannot be empty";
            return result;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            result.IsAllowed = false;
            result.ReasonDenied = "User ID is required";
            return result;
        }

        // Check for prompt injection
        if (DetectPromptInjection(intent))
        {
            _logger.LogWarning("Prompt injection detected in intent from user {UserId}", userId);
            result.IsAllowed = false;
            result.ReasonDenied = "Intent contains potentially malicious patterns";
            result.ValidationType = GuardrailValidationType.PromptInjectionDetected;
            result.TriggeredChecks.Add("PromptInjectionDetection");
            return result;
        }

        // Check for restricted operations
        if (ContainsRestrictedOperation(intent))
        {
            _logger.LogWarning("Restricted operation detected in intent from user {UserId}", userId);
            result.IsAllowed = false;
            result.ReasonDenied = "Intent contains restricted operations (delete, drop, etc)";
            result.ValidationType = GuardrailValidationType.SensitiveOperationDetected;
            result.TriggeredChecks.Add("RestrictedOperationDetection");
            return result;
        }

        // Check rate limit
        if (await IsRateLimitedAsync(userId))
        {
            _logger.LogWarning("Rate limit exceeded for user {UserId}", userId);
            result.IsAllowed = false;
            result.ReasonDenied = "Rate limit exceeded";
            result.ValidationType = GuardrailValidationType.RateLimitExceeded;
            result.TriggeredChecks.Add("RateLimiting");
            return result;
        }

        _logger.LogInformation("Intent validation passed for user {UserId}", userId);
        return result;
    }

    public async Task<bool> IsRateLimitedAsync(string userId)
    {
        const int requestsPerHour = 100;
        var now = DateTime.UtcNow;

        var key = userId.ToLowerInvariant();

        if (_rateLimitTracking.TryGetValue(key, out var tracking))
        {
            var (count, resetTime) = tracking;

            // Reset counter if window has passed
            if (now >= resetTime)
            {
                _rateLimitTracking[key] = (1, now.AddHours(1));
                return false;
            }

            // Check if limit exceeded
            if (count >= requestsPerHour)
            {
                return true;
            }

            // Increment counter
            _rateLimitTracking[key] = (count + 1, resetTime);
        }
        else
        {
            // First request in window
            _rateLimitTracking[key] = (1, now.AddHours(1));
        }

        return false;
    }

    public async Task RecordIntentExecutionAsync(string userId, string intent, bool success, string? result)
    {
        // TODO: Implement audit logging to database or external service
        _logger.LogInformation(
            "Intent execution recorded - UserId: {UserId}, Success: {Success}, Intent: {Intent}, Result: {Result}",
            userId, success, intent, result ?? "null");

        await Task.CompletedTask;
    }

    /// <summary>
    /// Detect common prompt injection patterns
    /// </summary>
    private bool DetectPromptInjection(string intent)
    {
        foreach (var pattern in InjectionPatterns)
        {
            if (Regex.IsMatch(intent, pattern))
            {
                _logger.LogDebug("Injection pattern matched: {Pattern}", pattern);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if intent contains restricted operations
    /// </summary>
    private bool ContainsRestrictedOperation(string intent)
    {
        var lowerIntent = intent.ToLowerInvariant();

        foreach (var operation in RestrictedOperations)
        {
            if (Regex.IsMatch(lowerIntent, $@"\b{operation}\b"))
            {
                _logger.LogDebug("Restricted operation detected: {Operation}", operation);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if intent references restricted functions
    /// </summary>
    private bool ContainsRestrictedFunction(string intent)
    {
        var lowerIntent = intent.ToLowerInvariant();

        foreach (var function in RestrictedFunctions)
        {
            if (Regex.IsMatch(lowerIntent, $@"\b{function}\b"))
            {
                _logger.LogDebug("Restricted function detected: {Function}", function);
                return true;
            }
        }

        return false;
    }
}
