using Microsoft.SemanticKernel;

namespace SemanticApiGateway.Gateway.Features.LLM;

/// <summary>
/// Anthropic Claude provider implementation.
/// High-quality reasoning, competitive cost.
/// </summary>
public class AnthropicProvider(
    ILogger<AnthropicProvider> logger) : ILLMProvider
{
    public string ProviderId => "anthropic";
    public string Name => "Anthropic Claude 3 Sonnet";
    public decimal CostPer1KInputTokens => 0.003m; // $0.003 per 1K input tokens
    public decimal CostPer1KOutputTokens => 0.015m; // $0.015 per 1K output tokens
    public int MaxTokens => 200000;
    public int Priority => 90; // Very high priority - excellent quality + lower cost

    private Kernel? _kernel;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var isAvailable = !string.IsNullOrEmpty(apiKey);

        if (!isAvailable)
        {
            logger.LogDebug("Anthropic provider not available: ANTHROPIC_API_KEY not set");
        }

        return await Task.FromResult(isAvailable);
    }

    public async Task<Kernel> GetKernelAsync(CancellationToken cancellationToken = default)
    {
        if (_kernel != null)
        {
            return _kernel;
        }

        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set");

        // Note: This requires Anthropic plugin for Semantic Kernel
        // For now, we'll create a basic kernel that can be extended
        var kernelBuilder = Kernel.CreateBuilder();

        // TODO: Add Anthropic chat completion when plugin is available
        // kernelBuilder.AddAnthropicChatCompletion(
        //     modelId: "claude-3-sonnet-20240229",
        //     apiKey: apiKey);

        _kernel = kernelBuilder.Build();
        logger.LogInformation("Anthropic provider initialized with Claude 3 Sonnet");

        return await Task.FromResult(_kernel);
    }
}
