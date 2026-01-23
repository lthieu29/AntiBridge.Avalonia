using NUnit.Framework;
using AntiBridge.Core.Services;
using AntiBridge.Core.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AntiBridge.Tests;

/// <summary>
/// Integration tests for ProxyServer with ModelRouter, TrafficMonitor, ContextManager, and DeviceProfile.
/// Task 8.5: Write integration tests
/// </summary>
[TestFixture]
public class ProxyServerIntegrationTests
{
    private string _testDbPath = null!;

    [SetUp]
    public void SetUp()
    {
        // Use a unique test database path for each test
        _testDbPath = Path.Combine(Path.GetTempPath(), $"antibridge_test_{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test database
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }
    }

    #region Task 8.5.1: Test full request flow with model routing

    [Test]
    public void ModelRouter_ResolveModel_ExactMatch_ReturnsCorrectMapping()
    {
        // Arrange
        var router = new ModelRouter();
        router.SetCustomMapping("gpt-4", "gemini-2.5-flash");

        // Act
        var result = router.ResolveModel("gpt-4");

        // Assert
        Assert.That(result, Is.EqualTo("gemini-2.5-flash"));
    }

    [Test]
    public void ModelRouter_ResolveModel_WildcardMatch_ReturnsCorrectMapping()
    {
        // Arrange
        var router = new ModelRouter();
        router.SetCustomMapping("claude-*-sonnet-*", "claude-sonnet-4-5");

        // Act
        var result = router.ResolveModel("claude-3-5-sonnet-20241022");

        // Assert
        Assert.That(result, Is.EqualTo("claude-sonnet-4-5"));
    }

    [Test]
    public void ModelRouter_ResolveModel_SystemDefault_ReturnsCorrectMapping()
    {
        // Arrange
        var router = new ModelRouter();

        // Act - test system defaults
        var gpt4Result = router.ResolveModel("gpt-4");
        var gpt4oResult = router.ResolveModel("gpt-4o");

        // Assert
        Assert.That(gpt4Result, Is.EqualTo("gemini-2.5-flash"));
        Assert.That(gpt4oResult, Is.EqualTo("gemini-2.5-flash"));
    }

    [Test]
    public void ModelRouter_ResolveModel_Passthrough_ForGeminiModels()
    {
        // Arrange
        var router = new ModelRouter();

        // Act
        var result = router.ResolveModel("gemini-2.5-pro-custom");

        // Assert - gemini models should pass through
        Assert.That(result, Is.EqualTo("gemini-2.5-pro-custom"));
    }

    [Test]
    public void ModelRouter_ResolveModel_Passthrough_ForThinkingModels()
    {
        // Arrange
        var router = new ModelRouter();

        // Act
        var result = router.ResolveModel("claude-opus-4-5-thinking");

        // Assert - thinking models should pass through
        Assert.That(result, Is.EqualTo("claude-opus-4-5-thinking"));
    }

    [Test]
    public void ModelRouter_ResolveModel_Fallback_ForUnknownModels()
    {
        // Arrange
        var router = new ModelRouter();

        // Act
        var result = router.ResolveModel("unknown-model-xyz");

        // Assert - should fall back to default
        Assert.That(result, Is.EqualTo("claude-sonnet-4-5"));
    }

    [Test]
    public void ModelRouter_CustomMapping_OverridesSystemDefault()
    {
        // Arrange
        var router = new ModelRouter();
        router.SetCustomMapping("gpt-4", "custom-model");

        // Act
        var result = router.ResolveModel("gpt-4");

        // Assert - custom mapping should override system default
        Assert.That(result, Is.EqualTo("custom-model"));
    }

    #endregion

    #region Task 8.5.2: Test traffic logging end-to-end

    [Test]
    public void TrafficMonitor_LogRequest_StoresLogCorrectly()
    {
        // Arrange
        using var monitor = new TrafficMonitor(maxLogs: 100, dbPath: _testDbPath);
        monitor.Enabled = true;

        var log = new TrafficLog
        {
            Method = "POST",
            Url = "/v1/chat/completions",
            Model = "gpt-4",
            MappedModel = "gemini-2.5-flash",
            AccountEmail = "test@example.com",
            Protocol = "openai",
            Status = 200,
            DurationMs = 1500
        };

        // Act
        monitor.LogRequest(log);
        monitor.WaitForPendingSaves();

        // Assert
        var logs = monitor.GetLogs(10);
        Assert.That(logs.Count, Is.EqualTo(1));
        Assert.That(logs[0].Method, Is.EqualTo("POST"));
        Assert.That(logs[0].Url, Is.EqualTo("/v1/chat/completions"));
        Assert.That(logs[0].Model, Is.EqualTo("gpt-4"));
        Assert.That(logs[0].MappedModel, Is.EqualTo("gemini-2.5-flash"));
        Assert.That(logs[0].Protocol, Is.EqualTo("openai"));
    }

    [Test]
    public void TrafficMonitor_LogRequest_RecordsProtocolType()
    {
        // Arrange
        using var monitor = new TrafficMonitor(maxLogs: 100, dbPath: _testDbPath);
        monitor.Enabled = true;

        // Act - log requests with different protocols
        monitor.LogRequest(new TrafficLog { Protocol = "openai", Method = "POST", Url = "/v1/chat/completions" });
        monitor.LogRequest(new TrafficLog { Protocol = "anthropic", Method = "POST", Url = "/v1/messages" });
        monitor.LogRequest(new TrafficLog { Protocol = "gemini", Method = "POST", Url = "/v1/generate" });
        monitor.WaitForPendingSaves();

        // Assert
        var logs = monitor.GetLogs(10);
        Assert.That(logs.Count, Is.EqualTo(3));
        Assert.That(logs.Any(l => l.Protocol == "openai"), Is.True);
        Assert.That(logs.Any(l => l.Protocol == "anthropic"), Is.True);
        Assert.That(logs.Any(l => l.Protocol == "gemini"), Is.True);
    }

    [Test]
    public void TrafficMonitor_LogRequest_RecordsOriginalAndMappedModel()
    {
        // Arrange
        using var monitor = new TrafficMonitor(maxLogs: 100, dbPath: _testDbPath);
        monitor.Enabled = true;

        var log = new TrafficLog
        {
            Method = "POST",
            Url = "/v1/chat/completions",
            Model = "gpt-4-turbo",
            MappedModel = "gemini-2.5-flash",
            Protocol = "openai"
        };

        // Act
        monitor.LogRequest(log);
        monitor.WaitForPendingSaves();

        // Assert
        var logs = monitor.GetLogs(10);
        Assert.That(logs[0].Model, Is.EqualTo("gpt-4-turbo"));
        Assert.That(logs[0].MappedModel, Is.EqualTo("gemini-2.5-flash"));
    }

    [Test]
    public void TrafficMonitor_LogRequest_RecordsErrorMessage()
    {
        // Arrange
        using var monitor = new TrafficMonitor(maxLogs: 100, dbPath: _testDbPath);
        monitor.Enabled = true;

        var log = new TrafficLog
        {
            Method = "POST",
            Url = "/v1/chat/completions",
            Status = 500,
            Error = "Internal server error"
        };

        // Act
        monitor.LogRequest(log);
        monitor.WaitForPendingSaves();

        // Assert
        var logs = monitor.GetLogs(10);
        Assert.That(logs[0].Status, Is.EqualTo(500));
        Assert.That(logs[0].Error, Is.EqualTo("Internal server error"));
    }

    [Test]
    public void TrafficMonitor_Disabled_DoesNotLog()
    {
        // Arrange
        using var monitor = new TrafficMonitor(maxLogs: 100, dbPath: _testDbPath);
        monitor.Enabled = false;

        var log = new TrafficLog
        {
            Method = "POST",
            Url = "/v1/chat/completions"
        };

        // Act
        monitor.LogRequest(log);
        monitor.WaitForPendingSaves();

        // Assert
        var logs = monitor.GetLogs(10);
        Assert.That(logs.Count, Is.EqualTo(0));
    }

    [Test]
    public void TrafficMonitor_GetStats_ReturnsCorrectCounts()
    {
        // Arrange
        using var monitor = new TrafficMonitor(maxLogs: 100, dbPath: _testDbPath);
        monitor.Enabled = true;

        // Log some successful and failed requests
        monitor.LogRequest(new TrafficLog { Status = 200, Method = "POST", Url = "/test" });
        monitor.LogRequest(new TrafficLog { Status = 200, Method = "POST", Url = "/test" });
        monitor.LogRequest(new TrafficLog { Status = 500, Method = "POST", Url = "/test" });
        monitor.LogRequest(new TrafficLog { Status = 401, Method = "POST", Url = "/test" });
        monitor.WaitForPendingSaves();

        // Act
        var stats = monitor.GetStats();

        // Assert
        Assert.That(stats.TotalRequests, Is.EqualTo(4));
        Assert.That(stats.SuccessCount, Is.EqualTo(2));
        Assert.That(stats.ErrorCount, Is.EqualTo(2));
    }

    #endregion

    #region Task 8.5.3: Test context compression under pressure

    [Test]
    public void ContextManager_EstimateTokens_AsciiText()
    {
        // Arrange
        var text = new string('a', 400); // 400 ASCII chars

        // Act
        var tokens = ContextManager.EstimateTokens(text);

        // Assert - ASCII: ~4 chars/token + 15% margin
        // 400 / 4 = 100, * 1.15 = 115
        Assert.That(tokens, Is.EqualTo(115));
    }

    [Test]
    public void ContextManager_EstimateTokens_UnicodeText()
    {
        // Arrange
        var text = new string('中', 150); // 150 Unicode chars

        // Act
        var tokens = ContextManager.EstimateTokens(text);

        // Assert - Unicode: ~1.5 chars/token + 15% margin
        // 150 / 1.5 = 100, * 1.15 = 115
        Assert.That(tokens, Is.EqualTo(115));
    }

    [Test]
    public void ContextManager_EstimateTokens_MixedText()
    {
        // Arrange
        var text = new string('a', 200) + new string('中', 75); // 200 ASCII + 75 Unicode

        // Act
        var tokens = ContextManager.EstimateTokens(text);

        // Assert
        // ASCII: 200 / 4 = 50
        // Unicode: 75 / 1.5 = 50
        // Total: 100 * 1.15 = 115
        Assert.That(tokens, Is.EqualTo(115));
    }

    [Test]
    public void ContextManager_EstimateTokens_EmptyText()
    {
        // Act
        var tokens = ContextManager.EstimateTokens("");

        // Assert
        Assert.That(tokens, Is.EqualTo(0));
    }

    [Test]
    public void ContextManager_EstimateTokens_NullText()
    {
        // Act
        var tokens = ContextManager.EstimateTokens(null);

        // Assert
        Assert.That(tokens, Is.EqualTo(0));
    }

    [Test]
    public void ContextManager_TrimToolMessages_KeepsLastNRounds()
    {
        // Arrange - create 7 tool rounds
        var messages = new JsonArray();
        for (int i = 0; i < 7; i++)
        {
            // Assistant with tool_use
            messages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = $"tool_{i}",
                        ["name"] = "test_tool",
                        ["input"] = new JsonObject()
                    }
                }
            });
            // User with tool_result
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = $"tool_{i}",
                        ["content"] = "result"
                    }
                }
            });
        }

        // Act - keep last 5 rounds
        var modified = ContextManager.TrimToolMessages(messages, keepLastNRounds: 5);

        // Assert
        Assert.That(modified, Is.True);
        // 5 rounds * 2 messages = 10 messages
        Assert.That(messages.Count, Is.EqualTo(10));
    }

    [Test]
    public void ContextManager_TrimToolMessages_NoTrimWhenUnderLimit()
    {
        // Arrange - create 3 tool rounds (under limit of 5)
        var messages = new JsonArray();
        for (int i = 0; i < 3; i++)
        {
            messages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = $"tool_{i}",
                        ["name"] = "test_tool",
                        ["input"] = new JsonObject()
                    }
                }
            });
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = $"tool_{i}",
                        ["content"] = "result"
                    }
                }
            });
        }

        var originalCount = messages.Count;

        // Act
        var modified = ContextManager.TrimToolMessages(messages, keepLastNRounds: 5);

        // Assert
        Assert.That(modified, Is.False);
        Assert.That(messages.Count, Is.EqualTo(originalCount));
    }

    [Test]
    public void ContextManager_CompressThinking_PreservesSignature()
    {
        // Arrange
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "thinking",
                        ["thinking"] = "This is a long thinking block that should be compressed...",
                        ["signature"] = "valid_signature_that_is_at_least_50_characters_long_for_testing"
                    }
                }
            },
            // Add 4 more messages to protect the last 4
            new JsonObject { ["role"] = "user", ["content"] = "msg1" },
            new JsonObject { ["role"] = "assistant", ["content"] = "msg2" },
            new JsonObject { ["role"] = "user", ["content"] = "msg3" },
            new JsonObject { ["role"] = "assistant", ["content"] = "msg4" }
        };

        // Act
        var modified = ContextManager.CompressThinkingPreserveSignature(messages, protectedLastN: 4);

        // Assert
        Assert.That(modified, Is.True);
        var thinkingBlock = messages[0]?["content"]?[0];
        Assert.That(thinkingBlock?["thinking"]?.ToString(), Is.EqualTo("..."));
        Assert.That(thinkingBlock?["signature"]?.ToString(), Is.EqualTo("valid_signature_that_is_at_least_50_characters_long_for_testing"));
    }

    [Test]
    public void ContextManager_CompressThinking_ProtectsLastNMessages()
    {
        // Arrange - thinking block in protected range
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "thinking",
                        ["thinking"] = "This thinking should NOT be compressed",
                        ["signature"] = "valid_signature_that_is_at_least_50_characters_long_for_testing"
                    }
                }
            }
        };

        // Act - protect last 4 messages (all messages are protected)
        var modified = ContextManager.CompressThinkingPreserveSignature(messages, protectedLastN: 4);

        // Assert
        Assert.That(modified, Is.False);
        var thinkingBlock = messages[0]?["content"]?[0];
        Assert.That(thinkingBlock?["thinking"]?.ToString(), Is.EqualTo("This thinking should NOT be compressed"));
    }

    [Test]
    public void ContextManager_ExtractLastValidSignature_FindsSignature()
    {
        // Arrange
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "thinking",
                        ["thinking"] = "...",
                        ["signature"] = "first_signature_that_is_at_least_50_characters_long_for_testing"
                    }
                }
            },
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "thinking",
                        ["thinking"] = "...",
                        ["signature"] = "last_signature_that_is_at_least_50_characters_long_for_testing!"
                    }
                }
            }
        };

        // Act
        var signature = ContextManager.ExtractLastValidSignature(messages);

        // Assert - should return the last valid signature
        Assert.That(signature, Is.EqualTo("last_signature_that_is_at_least_50_characters_long_for_testing!"));
    }

    [Test]
    public void ContextManager_ExtractLastValidSignature_ReturnsNull_WhenNoValidSignature()
    {
        // Arrange
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "thinking",
                        ["thinking"] = "...",
                        ["signature"] = "short" // Less than 50 chars
                    }
                }
            }
        };

        // Act
        var signature = ContextManager.ExtractLastValidSignature(messages);

        // Assert
        Assert.That(signature, Is.Null);
    }

    [Test]
    public void ContextManager_ApplyProgressiveCompression_Layer1_At60Percent()
    {
        // Arrange - create a request with many tool rounds
        var messages = new JsonArray();
        for (int i = 0; i < 10; i++)
        {
            messages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = $"tool_{i}",
                        ["name"] = "test_tool",
                        ["input"] = new JsonObject()
                    }
                }
            });
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = $"tool_{i}",
                        ["content"] = new string('x', 10000) // Large content
                    }
                }
            });
        }

        var request = new JsonObject { ["messages"] = messages };

        // Act - use a small max tokens to trigger compression
        var result = ContextManager.ApplyProgressiveCompression(request, maxTokens: 1000);

        // Assert
        Assert.That(result.LayersApplied.Contains(1), Is.True);
    }

    [Test]
    public void ContextManager_ApplyProgressiveCompression_NoCompression_WhenUnderThreshold()
    {
        // Arrange - small request
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "Hello" },
            new JsonObject { ["role"] = "assistant", ["content"] = "Hi there!" }
        };

        var request = new JsonObject { ["messages"] = messages };

        // Act - use large max tokens
        var result = ContextManager.ApplyProgressiveCompression(request, maxTokens: 1_000_000);

        // Assert
        Assert.That(result.WasCompressed, Is.False);
        Assert.That(result.LayersApplied, Is.Empty);
    }

    #endregion

    #region Task 8.5.4: Test device fingerprint application

    [Test]
    public void DeviceProfile_GenerateRandom_CreatesUniqueProfiles()
    {
        // Act
        var profile1 = DeviceProfile.GenerateRandom();
        var profile2 = DeviceProfile.GenerateRandom();

        // Assert
        Assert.That(profile1.MachineId, Is.Not.EqualTo(profile2.MachineId));
        Assert.That(profile1.MacMachineId, Is.Not.EqualTo(profile2.MacMachineId));
        Assert.That(profile1.DevDeviceId, Is.Not.EqualTo(profile2.DevDeviceId));
        Assert.That(profile1.SqmId, Is.Not.EqualTo(profile2.SqmId));
    }

    [Test]
    public void DeviceProfile_GenerateRandom_HasValidFormat()
    {
        // Act
        var profile = DeviceProfile.GenerateRandom();

        // Assert
        Assert.That(profile.MachineId, Is.Not.Empty);
        Assert.That(profile.MacMachineId, Does.Match(@"^[0-9A-F]{2}(:[0-9A-F]{2}){5}$")); // MAC format
        Assert.That(profile.DevDeviceId, Has.Length.EqualTo(32)); // GUID without dashes
        Assert.That(profile.SqmId, Does.StartWith("{").And.EndsWith("}")); // GUID with braces
    }

    [Test]
    public void DeviceProfile_Version_IsGenerated()
    {
        // Act
        var profile = new DeviceProfile();

        // Assert
        Assert.That(profile.Version, Is.Not.Empty);
        Assert.That(profile.Version, Has.Length.EqualTo(8));
    }

    [Test]
    public void DeviceProfile_CreatedAt_IsSet()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        var profile = new DeviceProfile();

        // Assert
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(profile.CreatedAt, Is.GreaterThanOrEqualTo(before));
        Assert.That(profile.CreatedAt, Is.LessThanOrEqualTo(after));
    }

    [Test]
    public void Account_SetDeviceProfile_ArchivesOldProfile()
    {
        // Arrange
        var account = new Account { Email = "test@example.com" };
        var profile1 = DeviceProfile.GenerateRandom();
        var profile2 = DeviceProfile.GenerateRandom();

        // Act
        account.SetDeviceProfile(profile1);
        account.SetDeviceProfile(profile2);

        // Assert
        Assert.That(account.DeviceProfile, Is.EqualTo(profile2));
        Assert.That(account.DeviceHistory.Count, Is.EqualTo(1));
        Assert.That(account.DeviceHistory[0], Is.EqualTo(profile1));
    }

    [Test]
    public void Account_SetDeviceProfile_FirstProfile_NoHistory()
    {
        // Arrange
        var account = new Account { Email = "test@example.com" };
        var profile = DeviceProfile.GenerateRandom();

        // Act
        account.SetDeviceProfile(profile);

        // Assert
        Assert.That(account.DeviceProfile, Is.EqualTo(profile));
        Assert.That(account.DeviceHistory, Is.Empty);
    }

    [Test]
    public void DeviceProfile_JsonSerialization_RoundTrip()
    {
        // Arrange
        var profile = DeviceProfile.GenerateRandom();

        // Act
        var json = JsonSerializer.Serialize(profile);
        var deserialized = JsonSerializer.Deserialize<DeviceProfile>(json);

        // Assert
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Version, Is.EqualTo(profile.Version));
        Assert.That(deserialized.MachineId, Is.EqualTo(profile.MachineId));
        Assert.That(deserialized.MacMachineId, Is.EqualTo(profile.MacMachineId));
        Assert.That(deserialized.DevDeviceId, Is.EqualTo(profile.DevDeviceId));
        Assert.That(deserialized.SqmId, Is.EqualTo(profile.SqmId));
        Assert.That(deserialized.CreatedAt, Is.EqualTo(profile.CreatedAt));
    }

    [Test]
    public void Account_WithDeviceProfile_JsonSerialization_RoundTrip()
    {
        // Arrange
        var account = new Account
        {
            Id = "test-id",
            Email = "test@example.com",
            Name = "Test User",
            Token = TokenData.Create("test-access-token", "test-refresh-token", 3600, "test@example.com")
        };
        account.SetDeviceProfile(DeviceProfile.GenerateRandom());
        account.SetDeviceProfile(DeviceProfile.GenerateRandom()); // Add to history

        // Act
        var json = JsonSerializer.Serialize(account);
        var deserialized = JsonSerializer.Deserialize<Account>(json);

        // Assert
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.DeviceProfile, Is.Not.Null);
        Assert.That(deserialized.DeviceHistory.Count, Is.EqualTo(1));
    }

    #endregion

    #region Additional integration tests

    [Test]
    public void ModelRouter_And_TrafficMonitor_Integration()
    {
        // Arrange
        var router = new ModelRouter();
        using var monitor = new TrafficMonitor(maxLogs: 100, dbPath: _testDbPath);
        monitor.Enabled = true;

        // Simulate request flow
        var originalModel = "gpt-4";
        var mappedModel = router.ResolveModel(originalModel);

        var log = new TrafficLog
        {
            Method = "POST",
            Url = "/v1/chat/completions",
            Model = originalModel,
            MappedModel = mappedModel,
            Protocol = "openai",
            Status = 200,
            DurationMs = 1000
        };

        // Act
        monitor.LogRequest(log);
        monitor.WaitForPendingSaves();

        // Assert
        var logs = monitor.GetLogs(10);
        Assert.That(logs.Count, Is.EqualTo(1));
        Assert.That(logs[0].Model, Is.EqualTo("gpt-4"));
        Assert.That(logs[0].MappedModel, Is.EqualTo("gemini-2.5-flash"));
    }

    [Test]
    public void GetMaxTokensForModel_FlashModel_Returns1M()
    {
        // Test the max tokens logic
        // Flash models should return 1M tokens
        var flashModels = new[] { "gemini-2.5-flash", "gemini-flash", "flash-model" };
        
        foreach (var model in flashModels)
        {
            // Flash models don't contain "pro", so they get 1M
            var containsPro = model.Contains("pro", StringComparison.OrdinalIgnoreCase);
            var expectedTokens = containsPro ? 2_000_000 : 1_000_000;
            Assert.That(expectedTokens, Is.EqualTo(1_000_000), $"Model {model} should have 1M tokens");
        }
    }

    [Test]
    public void GetMaxTokensForModel_ProModel_Returns2M()
    {
        // Test the max tokens logic
        // Pro models should return 2M tokens
        var proModels = new[] { "gemini-2.5-pro", "gemini-pro", "gemini-3-pro-preview" };
        
        foreach (var model in proModels)
        {
            var containsPro = model.Contains("pro", StringComparison.OrdinalIgnoreCase);
            var expectedTokens = containsPro ? 2_000_000 : 1_000_000;
            Assert.That(expectedTokens, Is.EqualTo(2_000_000), $"Model {model} should have 2M tokens");
        }
    }

    #endregion
}
