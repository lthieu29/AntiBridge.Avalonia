using System.Text.Json.Serialization;

namespace AntiBridge.Core.Models;

/// <summary>
/// Represents aggregated token statistics for a single hour.
/// Used for efficient storage and querying of token usage data.
/// </summary>
public class HourlyTokenStats
{
    /// <summary>
    /// Unix timestamp (in seconds) representing the start of the hour.
    /// Calculated as: timestamp / 3600 * 3600
    /// </summary>
    [JsonPropertyName("hour")]
    public long Hour { get; set; }

    /// <summary>
    /// Account email associated with this usage.
    /// </summary>
    [JsonPropertyName("account_email")]
    public string AccountEmail { get; set; } = string.Empty;

    /// <summary>
    /// Model name used for the requests.
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Total input tokens consumed during this hour for this account/model combination.
    /// </summary>
    [JsonPropertyName("input_tokens")]
    public long InputTokens { get; set; }

    /// <summary>
    /// Total output tokens generated during this hour for this account/model combination.
    /// </summary>
    [JsonPropertyName("output_tokens")]
    public long OutputTokens { get; set; }

    /// <summary>
    /// Number of requests made during this hour for this account/model combination.
    /// </summary>
    [JsonPropertyName("request_count")]
    public int RequestCount { get; set; }
}
