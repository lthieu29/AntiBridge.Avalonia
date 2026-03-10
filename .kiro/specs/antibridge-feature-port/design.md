# Design Document: AntiBridge Feature Port

## Overview

This design document describes the architecture and implementation details for porting key features from Antigravity-Manager (Tauri/Rust) to AntiBridge (C#/.NET):

1. **Device Fingerprint Isolation** - Per-account device fingerprint management
2. **Claude Protocol Translator Fixes** - Correct handling of thinking blocks, tool calls, and streaming
3. **OpenAI Protocol Translator Fixes** - Correct handling of reasoning content and tool calls
4. **Model Router** - Intelligent model name mapping with wildcard support
5. **Traffic Monitor** - Request/response logging with SQLite persistence
6. **Token Statistics Service** - Token usage tracking and aggregation
7. **Context Compression (3-Layer)** - Progressive context compression to prevent overflow

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        AntiBridge.Core                               │
├─────────────────────────────────────────────────────────────────────┤
│  Models/                                                             │
│  ├── Account.cs (+ DeviceProfile, DeviceHistory)                    │
│  ├── ProxyConfig.cs (+ ModelMappings)                               │
│  ├── TrafficLog.cs (NEW)                                            │
│  └── TokenStats.cs (NEW)                                            │
├─────────────────────────────────────────────────────────────────────┤
│  Services/                                                           │
│  ├── ProxyServer.cs (enhanced with Monitor, Router)                 │
│  ├── ModelRouter.cs (NEW)                                           │
│  ├── TrafficMonitor.cs (NEW)                                        │
│  ├── TokenStatsService.cs (NEW)                                     │
│  ├── ContextManager.cs (NEW)                                        │
│  └── SignatureCache.cs (enhanced)                                   │
├─────────────────────────────────────────────────────────────────────┤
│  Translator/                                                         │
│  ├── ClaudeToAntigravityRequest.cs (fixes)                          │
│  ├── AntigravityToClaudeResponse.cs (fixes)                         │
│  ├── OpenAIToAntigravityRequest.cs (fixes)                          │
│  └── AntigravityToOpenAIResponse.cs (fixes)                         │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Component Designs

### 1. Device Fingerprint Isolation

#### 1.1 Data Models

```csharp
// Models/DeviceProfile.cs
public class DeviceProfile
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = Guid.NewGuid().ToString("N")[..8];
    
    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    
    [JsonPropertyName("machine_id")]
    public string MachineId { get; set; } = string.Empty;
    
    [JsonPropertyName("mac_machine_id")]
    public string MacMachineId { get; set; } = string.Empty;
    
    [JsonPropertyName("dev_device_id")]
    public string DevDeviceId { get; set; } = string.Empty;
    
    [JsonPropertyName("sqm_id")]
    public string SqmId { get; set; } = string.Empty;
    
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
    
    private static string GenerateRandomMac()
    {
        var random = new Random();
        var bytes = new byte[6];
        random.NextBytes(bytes);
        return string.Join(":", bytes.Select(b => b.ToString("X2")));
    }
}
```

#### 1.2 Account Model Extension

```csharp
// Extend existing Account.cs
public class Account
{
    // ... existing properties ...
    
    [JsonPropertyName("device_profile")]
    public DeviceProfile? DeviceProfile { get; set; }
    
    [JsonPropertyName("device_history")]
    public List<DeviceProfile> DeviceHistory { get; set; } = new();
    
    public void SetDeviceProfile(DeviceProfile profile)
    {
        if (DeviceProfile != null)
            DeviceHistory.Add(DeviceProfile);
        DeviceProfile = profile;
    }
}
```

---

### 2. Model Router

#### 2.1 Interface and Implementation

```csharp
// Services/IModelRouter.cs
public interface IModelRouter
{
    string ResolveModel(string originalModel);
    void SetCustomMapping(string pattern, string target);
    void RemoveCustomMapping(string pattern);
    IReadOnlyDictionary<string, string> GetCustomMappings();
}
```

```csharp
// Services/ModelRouter.cs
public class ModelRouter : IModelRouter
{
    private readonly ConcurrentDictionary<string, string> _customMappings = new();
    private static readonly Dictionary<string, string> _systemDefaults = new()
    {
        // Claude mappings
        ["claude-3-5-sonnet-20241022"] = "claude-sonnet-4-5",
        ["claude-opus-4"] = "claude-opus-4-5-thinking",
        // OpenAI mappings
        ["gpt-4"] = "gemini-2.5-flash",
        ["gpt-4-turbo"] = "gemini-2.5-flash",
        ["gpt-4o"] = "gemini-2.5-flash",
        ["gpt-4o-mini"] = "gemini-2.5-flash",
        ["gpt-3.5-turbo"] = "gemini-2.5-flash",
        // Gemini mappings
        ["gemini-3-pro-low"] = "gemini-3-pro-preview",
        ["gemini-3-pro-high"] = "gemini-3-pro-preview",
    };
    
    public string ResolveModel(string originalModel)
    {
        // 1. Exact match in custom mappings (highest priority)
        if (_customMappings.TryGetValue(originalModel, out var exactMatch))
            return exactMatch;
        
        // 2. Wildcard match (most specific wins)
        var bestMatch = FindBestWildcardMatch(originalModel);
        if (bestMatch != null)
            return bestMatch;
        
        // 3. System default mappings
        if (_systemDefaults.TryGetValue(originalModel, out var systemMatch))
            return systemMatch;
        
        // 4. Pass-through for known prefixes
        if (originalModel.StartsWith("gemini-") || originalModel.Contains("thinking"))
            return originalModel;
        
        // 5. Fallback
        return "claude-sonnet-4-5";
    }
    
    private string? FindBestWildcardMatch(string model)
    {
        string? bestTarget = null;
        int bestSpecificity = -1;
        
        foreach (var (pattern, target) in _customMappings)
        {
            if (!pattern.Contains('*')) continue;
            if (WildcardMatch(pattern, model))
            {
                int specificity = pattern.Length - pattern.Count(c => c == '*');
                if (specificity > bestSpecificity)
                {
                    bestSpecificity = specificity;
                    bestTarget = target;
                }
            }
        }
        return bestTarget;
    }
    
    private static bool WildcardMatch(string pattern, string text)
    {
        var parts = pattern.Split('*');
        if (parts.Length == 1) return pattern == text;
        
        int textPos = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (string.IsNullOrEmpty(part)) continue;
            
            if (i == 0)
            {
                if (!text.StartsWith(part)) return false;
                textPos = part.Length;
            }
            else if (i == parts.Length - 1)
            {
                return text[textPos..].EndsWith(part);
            }
            else
            {
                var idx = text.IndexOf(part, textPos);
                if (idx < 0) return false;
                textPos = idx + part.Length;
            }
        }
        return true;
    }
    
    public void SetCustomMapping(string pattern, string target) 
        => _customMappings[pattern] = target;
    
    public void RemoveCustomMapping(string pattern) 
        => _customMappings.TryRemove(pattern, out _);
    
    public IReadOnlyDictionary<string, string> GetCustomMappings() 
        => _customMappings;
}
```

---

### 3. Traffic Monitor

#### 3.1 Data Models

```csharp
// Models/TrafficLog.cs
public class TrafficLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..16];
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int Status { get; set; }
    public long DurationMs { get; set; }
    public string? Model { get; set; }
    public string? MappedModel { get; set; }
    public string? AccountEmail { get; set; }
    public string? Error { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? Protocol { get; set; } // "openai", "anthropic", "gemini"
}

public class TrafficStats
{
    public long TotalRequests { get; set; }
    public long SuccessCount { get; set; }
    public long ErrorCount { get; set; }
}
```

#### 3.2 Traffic Monitor Service

```csharp
// Services/TrafficMonitor.cs
public class TrafficMonitor : IDisposable
{
    private readonly ConcurrentQueue<TrafficLog> _logs = new();
    private readonly int _maxLogs;
    private readonly string _dbPath;
    private volatile bool _enabled;
    
    public event Action<TrafficLog>? OnLogAdded;
    
    public TrafficMonitor(int maxLogs = 1000, string? dbPath = null)
    {
        _maxLogs = maxLogs;
        _dbPath = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AntiBridge", "traffic.db");
        InitializeDatabase();
    }
    
    public bool Enabled { get => _enabled; set => _enabled = value; }
    
    public void LogRequest(TrafficLog log)
    {
        // Always record token stats
        if (log.AccountEmail != null && log.InputTokens.HasValue && log.OutputTokens.HasValue)
        {
            TokenStatsService.Instance.RecordUsage(
                log.AccountEmail, log.Model ?? "unknown",
                log.InputTokens.Value, log.OutputTokens.Value);
        }
        
        if (!_enabled) return;
        
        _logs.Enqueue(log);
        while (_logs.Count > _maxLogs && _logs.TryDequeue(out _)) { }
        
        Task.Run(() => SaveToDatabase(log));
        OnLogAdded?.Invoke(log);
    }
    
    public List<TrafficLog> GetLogs(int limit = 100) => LoadFromDatabase(limit);
    public TrafficStats GetStats() => LoadStatsFromDatabase();
    public void Clear() { while (_logs.TryDequeue(out _)) { } ClearDatabase(); }
    
    private void InitializeDatabase() { /* SQLite init */ }
    private void SaveToDatabase(TrafficLog log) { /* INSERT */ }
    private List<TrafficLog> LoadFromDatabase(int limit) { /* SELECT */ }
    private TrafficStats LoadStatsFromDatabase() { /* Aggregate query */ }
    private void ClearDatabase() { /* DELETE */ }
    public void Dispose() { /* Cleanup */ }
}
```

---

### 4. Token Statistics Service

#### 4.1 Data Models

```csharp
// Models/TokenStats.cs
public class HourlyTokenStats
{
    public long Hour { get; set; }
    public string AccountEmail { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public int RequestCount { get; set; }
}

public class TokenStatsSummary
{
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalRequests { get; set; }
    public int UniqueAccounts { get; set; }
    public Dictionary<string, long> ByModel { get; set; } = new();
    public Dictionary<string, long> ByAccount { get; set; } = new();
}
```

#### 4.2 Token Stats Service

```csharp
// Services/TokenStatsService.cs
public class TokenStatsService
{
    private static readonly Lazy<TokenStatsService> _instance = new(() => new TokenStatsService());
    public static TokenStatsService Instance => _instance.Value;
    
    private readonly string _dbPath;
    private readonly object _writeLock = new();
    
    private TokenStatsService()
    {
        _dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AntiBridge", "token_stats.db");
        InitializeDatabase();
    }
    
    public void RecordUsage(string accountEmail, string model, int inputTokens, int outputTokens)
    {
        var hour = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600 * 3600;
        lock (_writeLock)
        {
            // UPSERT with aggregation
        }
    }
    
    public List<HourlyTokenStats> GetHourlyStats(long startTime, long endTime) { /* Query */ }
    public TokenStatsSummary GetSummary(long startTime, long endTime) { /* Aggregate */ }
    
    private void InitializeDatabase()
    {
        // CREATE TABLE hourly_stats with WAL mode
    }
}
```

---

### 5. Context Manager (3-Layer Compression)

#### 5.1 Token Estimation

```csharp
// Services/ContextManager.cs
public class ContextManager
{
    /// <summary>
    /// Estimate tokens with multi-language awareness
    /// ASCII: ~4 chars/token, Unicode/CJK: ~1.5 chars/token
    /// Adds 15% safety margin
    /// </summary>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        
        int asciiChars = 0, unicodeChars = 0;
        foreach (var c in text)
        {
            if (c < 128) asciiChars++;
            else unicodeChars++;
        }
        
        var asciiTokens = (int)Math.Ceiling(asciiChars / 4.0);
        var unicodeTokens = (int)Math.Ceiling(unicodeChars / 1.5);
        
        return (int)Math.Ceiling((asciiTokens + unicodeTokens) * 1.15);
    }
    
    public static int EstimateRequestTokens(JsonNode request)
    {
        int total = 0;
        // System prompt + Messages + Tools + Thinking budget
        // ... detailed implementation
        return total;
    }
}
```

#### 5.2 Layer 1: Tool Message Trimming

```csharp
public class ContextManager
{
    /// <summary>
    /// Layer 1: Trim old tool messages, keeping last N rounds
    /// Does NOT break Prompt Cache (only removes messages)
    /// </summary>
    public static bool TrimToolMessages(JsonArray messages, int keepLastNRounds = 5)
    {
        var toolRounds = IdentifyToolRounds(messages);
        if (toolRounds.Count <= keepLastNRounds) return false;
        
        var roundsToRemove = toolRounds.Count - keepLastNRounds;
        var indicesToRemove = new HashSet<int>();
        
        for (int i = 0; i < roundsToRemove; i++)
            foreach (var idx in toolRounds[i].Indices)
                indicesToRemove.Add(idx);
        
        // Remove in reverse order
        foreach (var idx in indicesToRemove.OrderByDescending(x => x))
            messages.RemoveAt(idx);
        
        return indicesToRemove.Count > 0;
    }
    
    private static List<ToolRound> IdentifyToolRounds(JsonArray messages)
    {
        // Identify assistant tool_use + user tool_result pairs
    }
}
```

#### 5.3 Layer 2: Thinking Compression with Signature Preservation

```csharp
public class ContextManager
{
    /// <summary>
    /// Layer 2: Compress thinking content while preserving signatures
    /// Breaks Prompt Cache but keeps signature chain intact
    /// </summary>
    public static bool CompressThinkingPreserveSignature(JsonArray messages, int protectedLastN = 4)
    {
        int startProtection = Math.Max(0, messages.Count - protectedLastN);
        bool modified = false;
        
        for (int i = 0; i < startProtection; i++)
        {
            var msg = messages[i];
            if (JsonHelper.GetString(msg, "role") != "assistant") continue;
            
            var content = msg["content"] as JsonArray;
            if (content == null) continue;
            
            foreach (var block in content)
            {
                if (JsonHelper.GetString(block, "type") != "thinking") continue;
                
                var signature = JsonHelper.GetString(block, "signature");
                var thinking = JsonHelper.GetString(block, "thinking");
                
                // Only compress if signature exists
                if (!string.IsNullOrEmpty(signature) && thinking?.Length > 10)
                {
                    (block as JsonObject)!["thinking"] = "...";
                    modified = true;
                }
            }
        }
        return modified;
    }
}
```

#### 5.4 Layer 3: Extract Last Valid Signature

```csharp
public class ContextManager
{
    /// <summary>
    /// Layer 3 Helper: Extract last valid signature for fork operations
    /// </summary>
    public static string? ExtractLastValidSignature(JsonArray messages)
    {
        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (JsonHelper.GetString(msg, "role") != "assistant") continue;
            
            var content = msg["content"] as JsonArray;
            if (content == null) continue;
            
            foreach (var block in content)
            {
                if (JsonHelper.GetString(block, "type") != "thinking") continue;
                var signature = JsonHelper.GetString(block, "signature");
                if (signature?.Length >= 50) return signature;
            }
        }
        return null;
    }
    
    /// <summary>
    /// Apply progressive compression based on context pressure
    /// </summary>
    public static void ApplyProgressiveCompression(
        JsonNode request, int maxTokens,
        int layer1Threshold = 60, int layer2Threshold = 75, int layer3Threshold = 90)
    {
        var messages = request["messages"] as JsonArray;
        if (messages == null) return;
        
        var estimatedTokens = EstimateRequestTokens(request);
        var pressure = (int)(estimatedTokens * 100.0 / maxTokens);
        
        // Layer 1: Tool message trimming (60%+)
        if (pressure >= layer1Threshold)
        {
            TrimToolMessages(messages, keepLastNRounds: 5);
            pressure = RecalculatePressure(request, maxTokens);
        }
        
        // Layer 2: Thinking compression (75%+)
        if (pressure >= layer2Threshold)
        {
            CompressThinkingPreserveSignature(messages, protectedLastN: 4);
            pressure = RecalculatePressure(request, maxTokens);
        }
        
        // Layer 3: Extract signature for potential fork (90%+)
        if (pressure >= layer3Threshold)
        {
            var lastSignature = ExtractLastValidSignature(messages);
            // Store for fork operation if needed
        }
    }
}
```

---

### 6. Protocol Translator Fixes

#### 6.1 Claude Protocol Fixes

Key fixes in `ClaudeToAntigravityRequest.cs`:
- Correct thinking block extraction with signature handling
- Proper tool_use/tool_result mapping with ID preservation
- Parts reordering (thinking blocks first for model role)
- Interleaved thinking hint injection when both tools and thinking enabled

Key fixes in `AntigravityToClaudeResponse.cs`:
- Correct SSE event emission sequence (message_start → content_block_start → delta → stop)
- Signature delta events with model group prefix
- Proper tool_use ID generation

#### 6.2 OpenAI Protocol Fixes

Key fixes in `OpenAIToAntigravityRequest.cs`:
- reasoning_effort parameter → thinking config mapping
- Tool call ID tracking for response matching
- Proper function arguments parsing

Key fixes in `AntigravityToOpenAIResponse.cs`:
- reasoning_content in streaming deltas
- Correct tool_calls array formatting with unique IDs
- Image URL handling with base64 data

---

## Correctness Properties

### Property 1: Model Router Determinism
For any input model name and fixed custom mappings, the router MUST return the same output.

### Property 2: Wildcard Specificity
When multiple wildcard patterns match, the pattern with highest specificity (most non-wildcard chars) MUST win.

### Property 3: Token Estimation Consistency
Same input text MUST produce same token count.

### Property 4: Context Compression Monotonicity
After any compression layer, estimated token count MUST be ≤ before.

### Property 5: Signature Preservation
Layer 2 compression MUST preserve all signatures.

### Property 6: Thread Safety
All concurrent operations on shared state MUST not cause data corruption.

---

## Testing Strategy

1. **Unit Tests**: Test each component in isolation
2. **Property-Based Tests**: Verify correctness properties with random inputs
3. **Integration Tests**: Test full request flow through proxy
4. **Regression Tests**: Ensure existing functionality not broken
