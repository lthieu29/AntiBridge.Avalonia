using System.Text.Json.Serialization;

namespace AntiBridge.Core.Models;

/// <summary>
/// Represents a device fingerprint profile for account isolation.
/// Each account can have its own unique device fingerprint to avoid detection when switching between accounts.
/// </summary>
public class DeviceProfile
{
    /// <summary>
    /// Unique version identifier for this profile.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Unix timestamp when this profile was created.
    /// </summary>
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// Machine identifier (GUID format).
    /// </summary>
    [JsonPropertyName("machine_id")]
    public string MachineId { get; set; } = string.Empty;

    /// <summary>
    /// MAC-based machine identifier (MAC address format XX:XX:XX:XX:XX:XX).
    /// </summary>
    [JsonPropertyName("mac_machine_id")]
    public string MacMachineId { get; set; } = string.Empty;

    /// <summary>
    /// Development device identifier (32-char hex string).
    /// </summary>
    [JsonPropertyName("dev_device_id")]
    public string DevDeviceId { get; set; } = string.Empty;

    /// <summary>
    /// SQM identifier (GUID format with braces).
    /// </summary>
    [JsonPropertyName("sqm_id")]
    public string SqmId { get; set; } = string.Empty;

    /// <summary>
    /// Generates a new DeviceProfile with random unique identifiers.
    /// </summary>
    /// <returns>A new DeviceProfile with randomly generated fingerprint values.</returns>
    public static DeviceProfile GenerateRandom()
    {
        return new DeviceProfile
        {
            MachineId = Guid.NewGuid().ToString(),
            MacMachineId = GenerateRandomMac(),
            DevDeviceId = Guid.NewGuid().ToString("N"),
            SqmId = $"{{{Guid.NewGuid().ToString().ToUpper()}}}"
        };
    }

    /// <summary>
    /// Generates a random MAC address in the format XX:XX:XX:XX:XX:XX.
    /// </summary>
    /// <returns>A random MAC address string.</returns>
    private static string GenerateRandomMac()
    {
        var random = new Random();
        var bytes = new byte[6];
        random.NextBytes(bytes);
        return string.Join(":", bytes.Select(b => b.ToString("X2")));
    }
}
