using Polly;
using Polly.CircuitBreaker;
using System.Collections.Concurrent;

namespace SemanticApiGateway.Gateway.Features.ErrorHandling;

/// <summary>
/// Interface for managing circuit breaker state per service.
/// Prevents cascading failures by stopping requests to degraded services.
/// </summary>
public interface ICircuitBreakerService
{
    /// <summary>
    /// Gets the circuit breaker policy for a specific service.
    /// Creates one if it doesn't exist.
    /// </summary>
    IAsyncPolicy<T> GetCircuitBreakerPolicy<T>(string serviceName) where T : class;

    /// <summary>
    /// Records a failure for the given service.
    /// Used to track error patterns and trigger circuit breaker.
    /// </summary>
    void RecordFailure(string serviceName);

    /// <summary>
    /// Records a success for the given service.
    /// Used to track recovery and reset circuit breaker.
    /// </summary>
    void RecordSuccess(string serviceName);

    /// <summary>
    /// Gets the current state of the circuit breaker for a service.
    /// </summary>
    CircuitState GetCircuitState(string serviceName);

    /// <summary>
    /// Resets the circuit breaker for a service (manual override).
    /// Useful for administrative reset after service recovery.
    /// </summary>
    void ResetCircuitBreaker(string serviceName);

    /// <summary>
    /// Gets metrics for a service's circuit breaker.
    /// </summary>
    CircuitBreakerMetrics GetMetrics(string serviceName);
}

/// <summary>
/// Production implementation of circuit breaker service.
/// Uses Polly v8 with advanced configuration options.
/// </summary>
public class CircuitBreakerService(ILogger<CircuitBreakerService> logger) : ICircuitBreakerService
{
    private readonly ConcurrentDictionary<string, ServiceCircuitBreaker> _circuitBreakers = new();

    private const int DefaultFailureThreshold = 5;
    private const int DefaultSuccessThreshold = 2;
    private static readonly TimeSpan DefaultTimeoutDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultHalfOpenTimeout = TimeSpan.FromSeconds(60);

    public IAsyncPolicy<T> GetCircuitBreakerPolicy<T>(string serviceName) where T : class
    {
        var breaker = _circuitBreakers.GetOrAdd(serviceName, _ =>
            new ServiceCircuitBreaker(serviceName, logger));

        return breaker.GetOrCreatePolicy<T>();
    }

    public void RecordFailure(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName))
            return;

        if (_circuitBreakers.TryGetValue(serviceName, out var breaker))
        {
            breaker.RecordFailure();
        }
    }

    public void RecordSuccess(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName))
            return;

        if (_circuitBreakers.TryGetValue(serviceName, out var breaker))
        {
            breaker.RecordSuccess();
        }
    }

    public CircuitState GetCircuitState(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName))
            return CircuitState.Closed;

        if (_circuitBreakers.TryGetValue(serviceName, out var breaker))
        {
            return breaker.State;
        }

        return CircuitState.Closed;
    }

    public void ResetCircuitBreaker(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName))
            return;

        if (_circuitBreakers.TryGetValue(serviceName, out var breaker))
        {
            breaker.Reset();
            logger.LogInformation("Circuit breaker manually reset for service: {Service}", serviceName);
        }
    }

    public CircuitBreakerMetrics GetMetrics(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            return new CircuitBreakerMetrics
            {
                ServiceName = serviceName ?? "unknown",
                State = CircuitState.Closed
            };
        }

        if (_circuitBreakers.TryGetValue(serviceName, out var breaker))
        {
            return breaker.GetMetrics();
        }

        return new CircuitBreakerMetrics
        {
            ServiceName = serviceName,
            State = CircuitState.Closed
        };
    }
}

/// <summary>
/// Internal class managing circuit breaker state for a single service.
/// </summary>
internal class ServiceCircuitBreaker
{
    private readonly string _serviceName;
    private readonly ILogger<CircuitBreakerService> _logger;
    private int _failureCount;
    private int _successCount;
    private DateTime _lastFailureTime = DateTime.UtcNow;
    private CircuitState _state = CircuitState.Closed;
    private DateTime _stateChangeTime = DateTime.UtcNow;
    private readonly object _lockObject = new();

    private const int FailureThreshold = 5;
    private const int SuccessThreshold = 2;
    private static readonly TimeSpan HalfOpenTimeout = TimeSpan.FromMinutes(1);

    public CircuitState State => _state;

    public ServiceCircuitBreaker(string serviceName, ILogger<CircuitBreakerService> logger)
    {
        _serviceName = serviceName;
        _logger = logger;
    }

    public void RecordFailure()
    {
        lock (_lockObject)
        {
            _lastFailureTime = DateTime.UtcNow;
            _failureCount++;
            _successCount = 0;

            // Transition to Open if threshold reached
            if (_state == CircuitState.Closed && _failureCount >= FailureThreshold)
            {
                TransitionToOpen();
            }
            else if (_state == CircuitState.HalfOpen && _failureCount >= 1)
            {
                TransitionToOpen();
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lockObject)
        {
            _failureCount = 0;
            _successCount++;

            // Transition from HalfOpen to Closed if threshold reached
            if (_state == CircuitState.HalfOpen && _successCount >= SuccessThreshold)
            {
                TransitionToClosed();
            }
        }
    }

    public void Reset()
    {
        lock (_lockObject)
        {
            _failureCount = 0;
            _successCount = 0;
            TransitionToClosed();
        }
    }

    public CircuitBreakerMetrics GetMetrics()
    {
        lock (_lockObject)
        {
            return new CircuitBreakerMetrics
            {
                ServiceName = _serviceName,
                State = _state,
                FailureCount = _failureCount,
                SuccessCount = _successCount,
                LastFailureTime = _lastFailureTime,
                StateChangeTime = _stateChangeTime,
                SecondsSinceLastFailure = (int)(DateTime.UtcNow - _lastFailureTime).TotalSeconds,
                SecondsSinceStateChange = (int)(DateTime.UtcNow - _stateChangeTime).TotalSeconds
            };
        }
    }

    public IAsyncPolicy<T> GetOrCreatePolicy<T>() where T : class
    {
        // Create circuit breaker that checks current state
        return Policy<T>
            .Handle<Exception>()
            .OrResult(r => r == null)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: FailureThreshold,
                durationOfBreak: HalfOpenTimeout,
                onBreak: (outcome, duration) =>
                {
                    lock (_lockObject)
                    {
                        if (_state != CircuitState.Open)
                        {
                            TransitionToOpen();
                        }
                    }
                },
                onReset: () =>
                {
                    lock (_lockObject)
                    {
                        if (_state != CircuitState.Closed)
                        {
                            TransitionToClosed();
                        }
                    }
                },
                onHalfOpen: () =>
                {
                    lock (_lockObject)
                    {
                        _state = CircuitState.HalfOpen;
                        _stateChangeTime = DateTime.UtcNow;
                        _logger.LogInformation(
                            "Circuit breaker entering HalfOpen state for service: {Service}",
                            _serviceName);
                    }
                });
    }

    private void TransitionToOpen()
    {
        _state = CircuitState.Open;
        _stateChangeTime = DateTime.UtcNow;
        _logger.LogWarning(
            "Circuit breaker opened for service: {Service}. Failure count: {FailureCount}",
            _serviceName,
            _failureCount);
    }

    private void TransitionToClosed()
    {
        _state = CircuitState.Closed;
        _stateChangeTime = DateTime.UtcNow;
        _failureCount = 0;
        _successCount = 0;
        _logger.LogInformation(
            "Circuit breaker closed for service: {Service}. Service recovered.",
            _serviceName);
    }
}

/// <summary>
/// State of a circuit breaker.
/// </summary>
public enum CircuitState
{
    /// <summary>
    /// Circuit is closed (normal operation). Requests flow through.
    /// </summary>
    Closed = 0,

    /// <summary>
    /// Circuit is open (service degraded). Requests fail fast.
    /// </summary>
    Open = 1,

    /// <summary>
    /// Circuit is half-open (testing recovery). Limited requests allowed.
    /// </summary>
    HalfOpen = 2
}

/// <summary>
/// Metrics for a service's circuit breaker state.
/// </summary>
public class CircuitBreakerMetrics
{
    public string ServiceName { get; set; } = string.Empty;
    public CircuitState State { get; set; }
    public int FailureCount { get; set; }
    public int SuccessCount { get; set; }
    public DateTime LastFailureTime { get; set; }
    public DateTime StateChangeTime { get; set; }
    public int SecondsSinceLastFailure { get; set; }
    public int SecondsSinceStateChange { get; set; }

    public bool IsOpen => State == CircuitState.Open;
    public bool IsClosed => State == CircuitState.Closed;
    public bool IsHalfOpen => State == CircuitState.HalfOpen;
}
