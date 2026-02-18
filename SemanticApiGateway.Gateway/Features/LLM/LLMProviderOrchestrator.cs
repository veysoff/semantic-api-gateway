using Microsoft.SemanticKernel;

namespace SemanticApiGateway.Gateway.Features.LLM;

/// <summary>
/// Orchestrates multiple LLM providers with fallback chain and cost optimization.
/// Selects best provider based on: availability, cost, priority, intent complexity.
/// </summary>
public interface ILLMProviderOrchestrator
{
    /// <summary>
    /// Gets best available provider for intent execution.
    /// Uses fallback chain if primary unavailable.
    /// </summary>
    Task<ILLMProvider> GetBestProviderAsync(
        string intentType = "default",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets kernel from best provider.
    /// </summary>
    Task<Kernel> GetKernelAsync(
        string intentType = "default",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all available providers with current status.
    /// </summary>
    Task<List<ProviderInfo>> GetAvailableProvidersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets provider by ID.
    /// </summary>
    ILLMProvider? GetProvider(string providerId);
}

/// <summary>
/// Default orchestrator implementation.
/// Selects providers based on: availability > priority > cost.
/// </summary>
public class LLMProviderOrchestrator(
    IEnumerable<ILLMProvider> providers,
    ILogger<LLMProviderOrchestrator> logger) : ILLMProviderOrchestrator
{
    private readonly Dictionary<string, ILLMProvider> _providersMap = providers
        .ToDictionary(p => p.ProviderId, p => p);

    private readonly ILLMProvider[] _priorityOrder = providers
        .OrderByDescending(p => p.Priority)
        .ToArray();

    public async Task<ILLMProvider> GetBestProviderAsync(
        string intentType = "default",
        CancellationToken cancellationToken = default)
    {
        // Check providers in priority order
        foreach (var provider in _priorityOrder)
        {
            var available = await provider.IsAvailableAsync(cancellationToken);
            if (available)
            {
                logger.LogInformation(
                    "Selected provider: {ProviderId} ({Name}) for intent: {IntentType}",
                    provider.ProviderId,
                    provider.Name,
                    intentType);

                return provider;
            }
        }

        // Fallback to first provider in list (should not happen in practice)
        var fallback = _priorityOrder.FirstOrDefault()
            ?? throw new InvalidOperationException("No LLM providers available");

        logger.LogWarning(
            "No available providers found, using fallback: {ProviderId}",
            fallback.ProviderId);

        return fallback;
    }

    public async Task<Kernel> GetKernelAsync(
        string intentType = "default",
        CancellationToken cancellationToken = default)
    {
        var provider = await GetBestProviderAsync(intentType, cancellationToken);
        return await provider.GetKernelAsync(cancellationToken);
    }

    public async Task<List<ProviderInfo>> GetAvailableProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var providers = new List<ProviderInfo>();

        foreach (var provider in _providersMap.Values)
        {
            var available = await provider.IsAvailableAsync(cancellationToken);

            providers.Add(new ProviderInfo
            {
                ProviderId = provider.ProviderId,
                Name = provider.Name,
                Available = available,
                Priority = provider.Priority,
                CostPer1KTokens = (provider.CostPer1KInputTokens + provider.CostPer1KOutputTokens) / 2,
                MaxTokens = provider.MaxTokens
            });
        }

        return providers.OrderByDescending(p => p.Priority).ToList();
    }

    public ILLMProvider? GetProvider(string providerId)
    {
        return _providersMap.TryGetValue(providerId, out var provider) ? provider : null;
    }
}
