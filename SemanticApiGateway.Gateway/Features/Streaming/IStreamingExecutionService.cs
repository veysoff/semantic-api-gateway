namespace SemanticApiGateway.Gateway.Features.Streaming;

/// <summary>
/// Service for streaming intent execution results in real-time.
/// Clients receive events as each step completes via Server-Sent Events (SSE).
/// </summary>
public interface IStreamingExecutionService
{
    /// <summary>
    /// Executes an intent and streams events for each step.
    /// Events are yielded as they occur, allowing clients to display progress.
    /// </summary>
    /// <param name="intent">Natural language intent to execute</param>
    /// <param name="userId">User requesting the execution</param>
    /// <param name="cancellationToken">Cancellation token to stop streaming</param>
    /// <returns>Async enumerable of stream events</returns>
    IAsyncEnumerable<StreamEvent> ExecuteIntentStreamingAsync(
        string intent,
        string userId,
        CancellationToken cancellationToken = default);
}
