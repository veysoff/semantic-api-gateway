using Microsoft.SemanticKernel;
using System.Collections.Concurrent;

namespace SemanticApiGateway.Gateway.Features.PluginOrchestration;

/// <summary>
/// Thread-safe registry for managing dynamically loaded plugins
/// Stores plugins by service name with concurrent access support
/// </summary>
public class PluginRegistry : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, List<KernelFunction>> _plugins;
    private readonly ReaderWriterLockSlim _lock = new();

    public PluginRegistry()
    {
        _plugins = new ConcurrentDictionary<string, List<KernelFunction>>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task RegisterPluginAsync(string serviceName, IEnumerable<KernelFunction> functions, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentNullException(nameof(serviceName));

        if (functions == null)
            throw new ArgumentNullException(nameof(functions));

        _lock.EnterWriteLock();
        try
        {
            var functionList = functions.ToList();
            _plugins.AddOrUpdate(serviceName, functionList, (_, existing) =>
            {
                existing.Clear();
                existing.AddRange(functionList);
                return existing;
            });

            await Task.CompletedTask;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IReadOnlyList<KernelFunction> GetPlugins(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return Array.Empty<KernelFunction>();

        _lock.EnterReadLock();
        try
        {
            if (_plugins.TryGetValue(serviceName, out var functions))
            {
                return functions.AsReadOnly();
            }
            return Array.Empty<KernelFunction>();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public IReadOnlyList<string> GetRegisteredServices()
    {
        _lock.EnterReadLock();
        try
        {
            return _plugins.Keys.ToList().AsReadOnly();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool HasService(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return false;

        _lock.EnterReadLock();
        try
        {
            return _plugins.ContainsKey(serviceName);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public async Task ClearServiceAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return;

        _lock.EnterWriteLock();
        try
        {
            _plugins.TryRemove(serviceName, out _);
            await Task.CompletedTask;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        _lock.EnterWriteLock();
        try
        {
            _plugins.Clear();
            await Task.CompletedTask;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public IReadOnlyDictionary<string, int> GetPluginMetadata()
    {
        _lock.EnterReadLock();
        try
        {
            return _plugins.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Count);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
}
