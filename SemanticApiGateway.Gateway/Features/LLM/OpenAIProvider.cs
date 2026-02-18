using Microsoft.SemanticKernel;

namespace SemanticApiGateway.Gateway.Features.LLM;

/// <summary>
/// OpenAI provider implementation (default).
/// Uses GPT-4 for high-quality responses.
/// </summary>
public class OpenAIProvider(
    ILogger<OpenAIProvider> logger) : ILLMProvider
{
    public string ProviderId => "openai";
    public string Name => "OpenAI GPT-4";
    public decimal CostPer1KInputTokens => 0.03m; // $0.03 per 1K input tokens
    public decimal CostPer1KOutputTokens => 0.06m; // $0.06 per 1K output tokens
    public int MaxTokens => 8192;
    public int Priority => 100; // Highest priority - best quality

    private Kernel? _kernel;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var isAvailable = !string.IsNullOrEmpty(apiKey);

        if (!isAvailable)
        {
            logger.LogWarning("OpenAI provider not available: OPENAI_API_KEY not set");
        }

        return await Task.FromResult(isAvailable);
    }

    public async Task<Kernel> GetKernelAsync(CancellationToken cancellationToken = default)
    {
        if (_kernel != null)
        {
            return _kernel;
        }

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set");

        var kernelBuilder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                modelId: "gpt-4",
                apiKey: apiKey);

        _kernel = kernelBuilder.Build();
        logger.LogInformation("OpenAI provider initialized with GPT-4");

        return await Task.FromResult(_kernel);
    }
}
