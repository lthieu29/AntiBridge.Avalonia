using System.Text.Json.Serialization;

namespace AntiBridge.Core.Models;

/// <summary>
/// Represents a single API traffic log entry.
/// Contains all metadata about a request/response cycle for debugging and analysis.
/// </summary>
public class TrafficLog
{
    /// <summary>
    /// Unique identifier for this log entry (16-char hex string).
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..16];

    /// <summary>
    /// Unix timestamp in milliseconds when the request was received.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// HTTP method (GET, POST, etc.).
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Request URL path.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// HTTP response status code.
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>
    /// Request duration in milliseconds.
    /// </summary>
    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    /// <summary>
    /// Original model name from the request.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Mapped model name after routing.
    /// </summary>
    [JsonPropertyName("mapped_model")]
    public string? MappedModel { get; set; }

    /// <summary>
    /// Account email associated with this request.
    /// </summary>
    [JsonPropertyName("account_email")]
    public string? AccountEmail { get; set; }

    /// <summary>
    /// Error message if the request failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Request body (may be truncated for large payloads).
    /// </summary>
    [JsonPropertyName("request_body")]
    public string? RequestBody { get; set; }

    /// <summary>
    /// Response body (may be truncated for large payloads).
    /// </summary>
    [JsonPropertyName("response_body")]
    public string? ResponseBody { get; set; }

    /// <summary>
    /// Number of input tokens consumed.
    /// </summary>
    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; set; }

    /// <summary>
    /// Number of output tokens generated.
    /// </summary>
    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; set; }

    /// <summary>
    /// Protocol type: "openai", "anthropic", or "gemini".
    /// </summary>
    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }
}
