using System.Text.Json.Serialization;

namespace AntiBridge.Core.Models;

/// <summary>
/// Represents aggregate statistics for traffic logs.
/// </summary>
public class TrafficStats
{
    /// <summary>
    /// Total number of requests logged.
    /// </summary>
    [JsonPropertyName("total_requests")]
    public long TotalRequests { get; set; }

    /// <summary>
    /// Number of successful requests (status 2xx).
    /// </summary>
    [JsonPropertyName("success_count")]
    public long SuccessCount { get; set; }

    /// <summary>
    /// Number of failed requests (status 4xx or 5xx).
    /// </summary>
    [JsonPropertyName("error_count")]
    public long ErrorCount { get; set; }
}
