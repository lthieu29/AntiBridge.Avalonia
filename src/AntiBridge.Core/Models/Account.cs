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

    /// <summary>
    /// Optional device fingerprint profile for this account.
    /// When set, this profile is used to isolate the account's device identity.
    /// </summary>
    [JsonPropertyName("device_profile")]
    public DeviceProfile? DeviceProfile { get; set; }

    /// <summary>
    /// History of previous device profiles for this account.
    /// Old profiles are archived here when a new profile is set.
    /// </summary>
    [JsonPropertyName("device_history")]
    public List<DeviceProfile> DeviceHistory { get; set; } = new();

    public void UpdateLastUsed() => LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// Sets a new device profile for this account.
    /// If a profile already exists, it is archived to the device history.
    /// </summary>
    /// <param name="profile">The new device profile to set.</param>
    public void SetDeviceProfile(DeviceProfile profile)
    {
        if (DeviceProfile != null)
        {
            DeviceHistory.Add(DeviceProfile);
        }
        DeviceProfile = profile;
    }
}
