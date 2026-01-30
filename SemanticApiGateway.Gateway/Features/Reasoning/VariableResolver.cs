using System.Text.Json;
using System.Text.RegularExpressions;

namespace SemanticApiGateway.Gateway.Features.Reasoning;

/// <summary>
/// Resolves variable references in step parameters using previous step results
/// Supports ${stepN.property} syntax for data piping between steps
/// </summary>
public class VariableResolver
{
    private readonly ILogger<VariableResolver> _logger;
    private static readonly Regex VariablePattern = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    public VariableResolver(ILogger<VariableResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves all variable references in step parameters using execution context
    /// </summary>
    public Dictionary<string, object> ResolveParameters(
        Dictionary<string, object> parameters,
        ExecutionContext context)
    {
        if (parameters == null || !parameters.Any())
            return parameters ?? new Dictionary<string, object>();

        var resolved = new Dictionary<string, object>();

        foreach (var (key, value) in parameters)
        {
            resolved[key] = ResolveValue(value, context);
        }

        return resolved;
    }

    /// <summary>
    /// Resolves a single value, handling strings, objects, and collections
    /// </summary>
    private object ResolveValue(object value, ExecutionContext context)
    {
        return value switch
        {
            string str => ResolveString(str, context),
            Dictionary<string, object> dict => ResolveDictionary(dict, context),
            IEnumerable<object> list => list.Select(item => ResolveValue(item, context)).ToList(),
            _ => value
        };
    }

    /// <summary>
    /// Resolves variable references in a string value
    /// Supports: ${step1.orderId}, ${step2.user.email}, ${userId}
    /// </summary>
    private string ResolveString(string value, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var matches = VariablePattern.Matches(value);
        if (matches.Count == 0)
            return value;

        var result = value;

        foreach (Match match in matches)
        {
            var variableExpression = match.Groups[1].Value;
            var resolvedValue = ResolveVariable(variableExpression, context);

            if (resolvedValue != null)
            {
                // If the entire string is just the variable, return the actual type
                if (match.Value == value)
                {
                    return resolvedValue.ToString() ?? string.Empty;
                }

                // Otherwise, replace the variable in the string
                result = result.Replace(match.Value, resolvedValue.ToString());
            }
            else
            {
                _logger.LogWarning("Could not resolve variable: {Variable}", variableExpression);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves variables in a dictionary
    /// </summary>
    private Dictionary<string, object> ResolveDictionary(
        Dictionary<string, object> dict,
        ExecutionContext context)
    {
        var resolved = new Dictionary<string, object>();

        foreach (var (key, value) in dict)
        {
            resolved[key] = ResolveValue(value, context);
        }

        return resolved;
    }

    /// <summary>
    /// Resolves a variable expression like "step1.orderId" or "userId"
    /// </summary>
    private object? ResolveVariable(string expression, ExecutionContext context)
    {
        var parts = expression.Split('.');

        // Handle built-in context variables
        if (parts.Length == 1)
        {
            return parts[0].ToLowerInvariant() switch
            {
                "userid" => context.UserId,
                "intent" => context.Intent,
                _ => null
            };
        }

        // Handle step references: step1.orderId, step2.user.email
        if (parts[0].StartsWith("step", StringComparison.OrdinalIgnoreCase))
        {
            var stepNumberStr = parts[0].Substring(4);
            if (!int.TryParse(stepNumberStr, out var stepNumber))
            {
                _logger.LogWarning("Invalid step number in expression: {Expression}", expression);
                return null;
            }

            var stepResult = context.StepResults.FirstOrDefault(s => s.Order == stepNumber);
            if (stepResult == null)
            {
                _logger.LogWarning("Step {StepNumber} not found in execution context", stepNumber);
                return null;
            }

            if (stepResult.Result == null)
            {
                _logger.LogWarning("Step {StepNumber} has null result", stepNumber);
                return null;
            }

            // Navigate the property path
            return NavigatePropertyPath(stepResult.Result, parts.Skip(1).ToArray());
        }

        return null;
    }

    /// <summary>
    /// Navigates a property path like "user.email" in an object
    /// </summary>
    private object? NavigatePropertyPath(object obj, string[] propertyPath)
    {
        if (propertyPath.Length == 0)
            return obj;

        var current = obj;

        foreach (var property in propertyPath)
        {
            if (current == null)
                return null;

            // Handle JsonElement (from API responses)
            if (current is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Object &&
                    jsonElement.TryGetProperty(property, out var jsonProperty))
                {
                    current = jsonProperty;
                    continue;
                }
                return null;
            }

            // Handle dictionaries
            if (current is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue(property, out var dictValue))
                {
                    current = dictValue;
                    continue;
                }
                return null;
            }

            // Handle regular objects using reflection
            var propertyInfo = current.GetType().GetProperty(
                property,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (propertyInfo != null)
            {
                current = propertyInfo.GetValue(current);
                continue;
            }

            _logger.LogWarning("Property {Property} not found on object type {Type}",
                property, current.GetType().Name);
            return null;
        }

        return current;
    }
}

/// <summary>
/// Execution context for variable resolution
/// Contains step results and user information
/// </summary>
public class ExecutionContext
{
    public string UserId { get; set; } = string.Empty;
    public string Intent { get; set; } = string.Empty;
    public List<StepResult> StepResults { get; set; } = new();
    public Dictionary<string, object> Variables { get; set; } = new();
}
