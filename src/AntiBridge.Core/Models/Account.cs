using System.Text.Json.Serialization;

namespace AntiBridge.Core.Models;

/// <summary>
/// Represents a Google account with OAuth tokens and quota data.
/// </summary>
public class Account
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("token")]
    public TokenData Token { get; set; } = new();

    [JsonPropertyName("quota")]
    public QuotaData? Quota { get; set; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [JsonPropertyName("last_used")]
    public long LastUsed { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public void UpdateLastUsed() => LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
