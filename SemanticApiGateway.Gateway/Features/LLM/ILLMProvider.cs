using Microsoft.SemanticKernel;

namespace SemanticApiGateway.Gateway.Features.LLM;

/// <summary>
/// Provider abstraction for different LLM backends.
/// Allows switching between OpenAI, Claude, local models, etc.
/// </summary>
public interface ILLMProvider
{
    /// <summary>
    /// Unique identifier for this provider (e.g., "openai", "claude", "ollama").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Human-readable name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether this provider is currently available/configured.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the Semantic Kernel with this provider configured.
    /// </summary>
    Task<Kernel> GetKernelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimated cost per 1000 tokens (input).
    /// Used for cost optimization.
    /// </summary>
    decimal CostPer1KInputTokens { get; }

    /// <summary>
    /// Estimated cost per 1000 tokens (output).
    /// </summary>
    decimal CostPer1KOutputTokens { get; }

    /// <summary>
    /// Maximum tokens per request.
    /// </summary>
    int MaxTokens { get; }

    /// <summary>
    /// Provider reliability/priority (0-100).
    /// Higher = more preferred as primary provider.
    /// </summary>
    int Priority { get; }
}

/// <summary>
/// Configuration for fallback chain and routing.
/// </summary>
public class LLMProviderConfig
{
    public string ProviderId { get; set; } = string.Empty;
    public int Priority { get; set; } = 50;
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Settings { get; set; } = [];
}

/// <summary>
/// Result of LLM provider invocation.
/// </summary>
public class LLMProviderResult
{
    public string ProviderId { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCost { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Information about available providers.
/// </summary>
public class ProviderInfo
{
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Available { get; set; }
    public int Priority { get; set; }
    public decimal CostPer1KTokens { get; set; }
    public int MaxTokens { get; set; }
}
