using System.Text.Json;
using NUnit.Framework;
using AntiBridge.Core.Models;

namespace AntiBridge.Tests;

[TestFixture]
public class DeviceProfileTests
{
    [Test]
    public void GenerateRandom_ProducesUniqueValues()
    {
        // Generate multiple profiles
        var profile1 = DeviceProfile.GenerateRandom();
        var profile2 = DeviceProfile.GenerateRandom();
        var profile3 = DeviceProfile.GenerateRandom();

        // All MachineIds should be unique
        Assert.That(profile1.MachineId, Is.Not.EqualTo(profile2.MachineId));
        Assert.That(profile2.MachineId, Is.Not.EqualTo(profile3.MachineId));
        Assert.That(profile1.MachineId, Is.Not.EqualTo(profile3.MachineId));

        // All MacMachineIds should be unique
        Assert.That(profile1.MacMachineId, Is.Not.EqualTo(profile2.MacMachineId));
        Assert.That(profile2.MacMachineId, Is.Not.EqualTo(profile3.MacMachineId));

        // All DevDeviceIds should be unique
        Assert.That(profile1.DevDeviceId, Is.Not.EqualTo(profile2.DevDeviceId));
        Assert.That(profile2.DevDeviceId, Is.Not.EqualTo(profile3.DevDeviceId));

        // All SqmIds should be unique
        Assert.That(profile1.SqmId, Is.Not.EqualTo(profile2.SqmId));
        Assert.That(profile2.SqmId, Is.Not.EqualTo(profile3.SqmId));

        // All Versions should be unique
        Assert.That(profile1.Version, Is.Not.EqualTo(profile2.Version));
        Assert.That(profile2.Version, Is.Not.EqualTo(profile3.Version));
    }

    [Test]
    public void GenerateRandom_ProducesValidFormats()
    {
        var profile = DeviceProfile.GenerateRandom();

        // MachineId should be a valid GUID
        Assert.That(Guid.TryParse(profile.MachineId, out _), Is.True, "MachineId should be a valid GUID");

        // MacMachineId should be in MAC address format (XX:XX:XX:XX:XX:XX)
        var macParts = profile.MacMachineId.Split(':');
        Assert.That(macParts.Length, Is.EqualTo(6), "MacMachineId should have 6 parts");
        foreach (var part in macParts)
        {
            Assert.That(part.Length, Is.EqualTo(2), "Each MAC part should be 2 characters");
            Assert.That(int.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out _), Is.True,
                "Each MAC part should be valid hex");
        }

        // DevDeviceId should be a 32-character hex string (GUID without dashes)
        Assert.That(profile.DevDeviceId.Length, Is.EqualTo(32), "DevDeviceId should be 32 characters");
        Assert.That(profile.DevDeviceId.All(c => char.IsLetterOrDigit(c)), Is.True,
            "DevDeviceId should be alphanumeric");

        // SqmId should be in {GUID} format with uppercase
        Assert.That(profile.SqmId.StartsWith("{"), Is.True, "SqmId should start with {");
        Assert.That(profile.SqmId.EndsWith("}"), Is.True, "SqmId should end with }");
        var sqmGuid = profile.SqmId.Trim('{', '}');
        Assert.That(Guid.TryParse(sqmGuid, out _), Is.True, "SqmId should contain a valid GUID");
        Assert.That(sqmGuid, Is.EqualTo(sqmGuid.ToUpper()), "SqmId GUID should be uppercase");

        // Version should be 8 characters
        Assert.That(profile.Version.Length, Is.EqualTo(8), "Version should be 8 characters");
    }

    [Test]
    public void GenerateRandom_SetsCreatedAtToCurrentTime()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var profile = DeviceProfile.GenerateRandom();
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.That(profile.CreatedAt, Is.GreaterThanOrEqualTo(before));
        Assert.That(profile.CreatedAt, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void JsonSerialization_RoundTrip_PreservesAllProperties()
    {
        var original = DeviceProfile.GenerateRandom();

        // Serialize to JSON
        var json = JsonSerializer.Serialize(original);

        // Deserialize back
        var deserialized = JsonSerializer.Deserialize<DeviceProfile>(json);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Version, Is.EqualTo(original.Version));
        Assert.That(deserialized.CreatedAt, Is.EqualTo(original.CreatedAt));
        Assert.That(deserialized.MachineId, Is.EqualTo(original.MachineId));
        Assert.That(deserialized.MacMachineId, Is.EqualTo(original.MacMachineId));
        Assert.That(deserialized.DevDeviceId, Is.EqualTo(original.DevDeviceId));
        Assert.That(deserialized.SqmId, Is.EqualTo(original.SqmId));
    }

    [Test]
    public void JsonSerialization_UsesCorrectPropertyNames()
    {
        var profile = new DeviceProfile
        {
            Version = "abc12345",
            CreatedAt = 1234567890,
            MachineId = "test-machine-id",
            MacMachineId = "AA:BB:CC:DD:EE:FF",
            DevDeviceId = "testdevdeviceid12345678901234",
            SqmId = "{TEST-SQM-ID}"
        };

        var json = JsonSerializer.Serialize(profile);

        // Verify JSON property names match the design spec
        Assert.That(json, Does.Contain("\"version\""));
        Assert.That(json, Does.Contain("\"created_at\""));
        Assert.That(json, Does.Contain("\"machine_id\""));
        Assert.That(json, Does.Contain("\"mac_machine_id\""));
        Assert.That(json, Does.Contain("\"dev_device_id\""));
        Assert.That(json, Does.Contain("\"sqm_id\""));
    }

    [Test]
    public void DefaultProfile_HasEmptyIdentifiers()
    {
        var profile = new DeviceProfile();

        Assert.That(profile.MachineId, Is.EqualTo(string.Empty));
        Assert.That(profile.MacMachineId, Is.EqualTo(string.Empty));
        Assert.That(profile.DevDeviceId, Is.EqualTo(string.Empty));
        Assert.That(profile.SqmId, Is.EqualTo(string.Empty));
        Assert.That(profile.Version.Length, Is.EqualTo(8));
        Assert.That(profile.CreatedAt, Is.GreaterThan(0));
    }
}

[TestFixture]
public class AccountDeviceProfileTests
{
    [Test]
    public void SetDeviceProfile_FirstProfile_SetsProfileWithoutHistory()
    {
        var account = new Account();
        var profile = DeviceProfile.GenerateRandom();

        account.SetDeviceProfile(profile);

        Assert.That(account.DeviceProfile, Is.EqualTo(profile));
        Assert.That(account.DeviceHistory, Is.Empty);
    }

    [Test]
    public void SetDeviceProfile_SecondProfile_ArchivesFirstToHistory()
    {
        var account = new Account();
        var profile1 = DeviceProfile.GenerateRandom();
        var profile2 = DeviceProfile.GenerateRandom();

        account.SetDeviceProfile(profile1);
        account.SetDeviceProfile(profile2);

        Assert.That(account.DeviceProfile, Is.EqualTo(profile2));
        Assert.That(account.DeviceHistory.Count, Is.EqualTo(1));
        Assert.That(account.DeviceHistory[0], Is.EqualTo(profile1));
    }

    [Test]
    public void SetDeviceProfile_MultipleProfiles_ArchivesAllPreviousToHistory()
    {
        var account = new Account();
        var profile1 = DeviceProfile.GenerateRandom();
        var profile2 = DeviceProfile.GenerateRandom();
        var profile3 = DeviceProfile.GenerateRandom();

        account.SetDeviceProfile(profile1);
        account.SetDeviceProfile(profile2);
        account.SetDeviceProfile(profile3);

        Assert.That(account.DeviceProfile, Is.EqualTo(profile3));
        Assert.That(account.DeviceHistory.Count, Is.EqualTo(2));
        Assert.That(account.DeviceHistory[0], Is.EqualTo(profile1));
        Assert.That(account.DeviceHistory[1], Is.EqualTo(profile2));
    }

    [Test]
    public void Account_JsonSerialization_PreservesDeviceProfileAndHistory()
    {
        var account = new Account
        {
            Id = "test-id",
            Email = "test@example.com",
            Token = new TokenData
            {
                AccessToken = "test-token",
                RefreshToken = "test-refresh",
                ExpiryTime = DateTime.UtcNow.AddHours(1) // Set valid expiry to avoid serialization issues
            }
        };
        var profile1 = DeviceProfile.GenerateRandom();
        var profile2 = DeviceProfile.GenerateRandom();

        account.SetDeviceProfile(profile1);
        account.SetDeviceProfile(profile2);

        // Serialize and deserialize
        var json = JsonSerializer.Serialize(account);
        var deserialized = JsonSerializer.Deserialize<Account>(json);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.DeviceProfile, Is.Not.Null);
        Assert.That(deserialized.DeviceProfile!.MachineId, Is.EqualTo(profile2.MachineId));
        Assert.That(deserialized.DeviceHistory.Count, Is.EqualTo(1));
        Assert.That(deserialized.DeviceHistory[0].MachineId, Is.EqualTo(profile1.MachineId));
    }

    [Test]
    public void NewAccount_HasNullDeviceProfileAndEmptyHistory()
    {
        var account = new Account();

        Assert.That(account.DeviceProfile, Is.Null);
        Assert.That(account.DeviceHistory, Is.Not.Null);
        Assert.That(account.DeviceHistory, Is.Empty);
    }
}
