using System.Text.Json.Serialization;

namespace AntiBridge.Core.Models;

/// <summary>
/// Represents a summary of token statistics for a time range.
/// Includes totals and breakdowns by model and account.
/// </summary>
public class TokenStatsSummary
{
    /// <summary>
    /// Total input tokens consumed across all accounts and models.
    /// </summary>
    [JsonPropertyName("total_input_tokens")]
    public long TotalInputTokens { get; set; }

    /// <summary>
    /// Total output tokens generated across all accounts and models.
    /// </summary>
    [JsonPropertyName("total_output_tokens")]
    public long TotalOutputTokens { get; set; }

    /// <summary>
    /// Total number of requests made across all accounts and models.
    /// </summary>
    [JsonPropertyName("total_requests")]
    public long TotalRequests { get; set; }

    /// <summary>
    /// Number of unique accounts that made requests.
    /// </summary>
    [JsonPropertyName("unique_accounts")]
    public int UniqueAccounts { get; set; }

    /// <summary>
    /// Token usage breakdown by model name.
    /// Key: model name, Value: total tokens (input + output).
    /// </summary>
    [JsonPropertyName("by_model")]
    public Dictionary<string, long> ByModel { get; set; } = new();

    /// <summary>
    /// Token usage breakdown by account email.
    /// Key: account email, Value: total tokens (input + output).
    /// </summary>
    [JsonPropertyName("by_account")]
    public Dictionary<string, long> ByAccount { get; set; } = new();
}
