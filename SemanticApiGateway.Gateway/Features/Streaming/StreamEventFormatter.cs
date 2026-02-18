using System.Text;
using System.Text.Json;

namespace SemanticApiGateway.Gateway.Features.Streaming;

/// <summary>
/// Formats StreamEvent objects as Server-Sent Events (SSE) format for HTTP streaming.
/// SSE format: event: {type}\ndata: {json}\n\n
/// </summary>
public static class StreamEventFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Formats a stream event as SSE-compatible string.
    /// </summary>
    public static string FormatAsSSE(StreamEvent @event)
    {
        var sb = new StringBuilder();

        // Event type
        sb.Append("event: ");
        sb.AppendLine(@event.EventType);

        // Event data as JSON
        sb.Append("data: ");
        var json = JsonSerializer.Serialize(@event, JsonOptions);
        sb.AppendLine(json);

        // Blank line marks end of event
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Converts StreamEvent to SSE string for async enumeration.
    /// </summary>
    public static async IAsyncEnumerable<string> StreamAsSSEAsync(
        IAsyncEnumerable<StreamEvent> events)
    {
        await foreach (var @event in events)
        {
            yield return FormatAsSSE(@event);
        }
    }
}
