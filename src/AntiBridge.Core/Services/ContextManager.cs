using System.Text.Json.Nodes;
using AntiBridge.Core.Translator;

namespace AntiBridge.Core.Services;

/// <summary>
/// Context Manager for 3-layer progressive compression.
/// Handles token estimation and context compression to prevent overflow.
/// </summary>
public static class ContextManager
{
    /// <summary>
    /// Represents a tool round consisting of assistant tool_use followed by user tool_result messages.
    /// </summary>
    public class ToolRound
    {
        /// <summary>
        /// Indices of messages that belong to this tool round.
        /// </summary>
        public List<int> Indices { get; } = new();
    }

    #region Token Estimation

    /// <summary>
    /// Estimate tokens with multi-language awareness.
    /// ASCII: ~4 chars/token, Unicode/CJK: ~1.5 chars/token
    /// Adds 15% safety margin.
    /// </summary>
    /// <param name="text">The text to estimate tokens for.</param>
    /// <returns>Estimated token count with 15% safety margin.</returns>
    public static int EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        int asciiChars = 0;
        int unicodeChars = 0;

        foreach (var c in text)
        {
            if (c < 128)
                asciiChars++;
            else
                unicodeChars++;
        }

        // ASCII: ~4 chars/token, Unicode: ~1.5 chars/token
        var asciiTokens = (int)Math.Ceiling(asciiChars / 4.0);
        var unicodeTokens = (int)Math.Ceiling(unicodeChars / 1.5);

        // Add 15% safety margin
        return (int)Math.Ceiling((asciiTokens + unicodeTokens) * 1.15);
    }

    /// <summary>
    /// Estimate total tokens for a full request including system prompt, messages, tools, and thinking budget.
    /// </summary>
    /// <param name="request">The JSON request object.</param>
    /// <returns>Estimated total token count.</returns>
    public static int EstimateRequestTokens(JsonNode? request)
    {
        if (request == null) return 0;

        int total = 0;

        // System instruction tokens
        var systemInstruction = JsonHelper.GetPath(request, "systemInstruction");
        if (systemInstruction != null)
        {
            total += EstimateJsonNodeTokens(systemInstruction);
        }

        // Also check for "system" field (Claude format)
        var system = JsonHelper.GetPath(request, "system");
        if (system != null)
        {
            if (system is JsonArray systemArray)
            {
                foreach (var item in systemArray)
                {
                    var text = JsonHelper.GetString(item, "text");
                    if (!string.IsNullOrEmpty(text))
                        total += EstimateTokens(text);
                }
            }
            else
            {
                total += EstimateTokens(system.ToString());
            }
        }

        // Messages tokens
        var messages = JsonHelper.GetPath(request, "messages") as JsonArray;
        if (messages != null)
        {
            foreach (var msg in messages)
            {
                total += EstimateMessageTokens(msg);
            }
        }

        // Contents tokens (Gemini format)
        var contents = JsonHelper.GetPath(request, "contents") as JsonArray;
        if (contents != null)
        {
            foreach (var content in contents)
            {
                total += EstimateJsonNodeTokens(content);
            }
        }

        // Tools tokens
        var tools = JsonHelper.GetPath(request, "tools") as JsonArray;
        if (tools != null)
        {
            total += EstimateJsonNodeTokens(tools);
        }

        // Thinking budget (if enabled, reserve some tokens)
        var thinking = JsonHelper.GetPath(request, "thinking");
        if (thinking != null)
        {
            var thinkingType = JsonHelper.GetString(thinking, "type");
            if (thinkingType == "enabled")
            {
                var budgetTokens = JsonHelper.GetInt(thinking, "budget_tokens");
                if (budgetTokens.HasValue)
                {
                    total += budgetTokens.Value;
                }
            }
        }

        // Generation config thinking budget (Gemini format)
        var genConfig = JsonHelper.GetPath(request, "generationConfig");
        if (genConfig != null)
        {
            var thinkingConfig = JsonHelper.GetPath(genConfig, "thinkingConfig");
            if (thinkingConfig != null)
            {
                var thinkingBudget = JsonHelper.GetInt(thinkingConfig, "thinkingBudget");
                if (thinkingBudget.HasValue)
                {
                    total += thinkingBudget.Value;
                }
            }
        }

        return total;
    }

    /// <summary>
    /// Estimate tokens for a single message.
    /// </summary>
    private static int EstimateMessageTokens(JsonNode? msg)
    {
        if (msg == null) return 0;

        int total = 0;

        // Role overhead
        total += 4; // Approximate overhead for role

        var content = JsonHelper.GetPath(msg, "content");
        if (content == null) return total;

        if (content is JsonArray contentArray)
        {
            foreach (var block in contentArray)
            {
                var type = JsonHelper.GetString(block, "type");
                switch (type)
                {
                    case "text":
                        var text = JsonHelper.GetString(block, "text");
                        total += EstimateTokens(text);
                        break;
                    case "thinking":
                        var thinking = JsonHelper.GetString(block, "thinking");
                        total += EstimateTokens(thinking);
                        // Signature is typically ~100 tokens
                        var signature = JsonHelper.GetString(block, "signature");
                        if (!string.IsNullOrEmpty(signature))
                            total += EstimateTokens(signature);
                        break;
                    case "tool_use":
                        var toolName = JsonHelper.GetString(block, "name");
                        total += EstimateTokens(toolName);
                        var input = JsonHelper.GetPath(block, "input");
                        if (input != null)
                            total += EstimateJsonNodeTokens(input);
                        break;
                    case "tool_result":
                        var resultContent = JsonHelper.GetPath(block, "content");
                        if (resultContent != null)
                            total += EstimateJsonNodeTokens(resultContent);
                        break;
                    default:
                        // Generic estimation for unknown types
                        total += EstimateJsonNodeTokens(block);
                        break;
                }
            }
        }
        else
        {
            // String content
            total += EstimateTokens(content.ToString());
        }

        return total;
    }

    /// <summary>
    /// Estimate tokens for a generic JSON node by serializing it.
    /// </summary>
    private static int EstimateJsonNodeTokens(JsonNode? node)
    {
        if (node == null) return 0;
        var json = JsonHelper.Stringify(node);
        return EstimateTokens(json);
    }

    #endregion

    #region Layer 1: Tool Message Trimming

    /// <summary>
    /// Identify tool rounds in the message array.
    /// A tool round consists of an assistant message with tool_use followed by a user message with tool_result.
    /// </summary>
    /// <param name="messages">The messages array.</param>
    /// <returns>List of tool rounds with their message indices.</returns>
    public static List<ToolRound> IdentifyToolRounds(JsonArray messages)
    {
        var rounds = new List<ToolRound>();
        if (messages == null || messages.Count == 0) return rounds;

        int i = 0;
        while (i < messages.Count)
        {
            var msg = messages[i];
            var role = JsonHelper.GetString(msg, "role");

            // Look for assistant message with tool_use
            if (role == "assistant" && HasToolUse(msg))
            {
                var round = new ToolRound();
                round.Indices.Add(i);

                // Look for following user message(s) with tool_result
                int j = i + 1;
                while (j < messages.Count)
                {
                    var nextMsg = messages[j];
                    var nextRole = JsonHelper.GetString(nextMsg, "role");

                    if (nextRole == "user" && HasToolResult(nextMsg))
                    {
                        round.Indices.Add(j);
                        j++;
                    }
                    else
                    {
                        break;
                    }
                }

                // Only add if we found at least one tool_result
                if (round.Indices.Count > 1)
                {
                    rounds.Add(round);
                }

                i = j;
            }
            else
            {
                i++;
            }
        }

        return rounds;
    }

    /// <summary>
    /// Check if a message contains tool_use blocks.
    /// </summary>
    private static bool HasToolUse(JsonNode? msg)
    {
        if (msg == null) return false;

        var content = JsonHelper.GetPath(msg, "content") as JsonArray;
        if (content == null) return false;

        foreach (var block in content)
        {
            var type = JsonHelper.GetString(block, "type");
            if (type == "tool_use")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if a message contains tool_result blocks.
    /// </summary>
    private static bool HasToolResult(JsonNode? msg)
    {
        if (msg == null) return false;

        var content = JsonHelper.GetPath(msg, "content") as JsonArray;
        if (content == null) return false;

        foreach (var block in content)
        {
            var type = JsonHelper.GetString(block, "type");
            if (type == "tool_result")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Layer 1: Trim old tool messages, keeping last N rounds.
    /// Does NOT break Prompt Cache (only removes messages).
    /// </summary>
    /// <param name="messages">The messages array to modify.</param>
    /// <param name="keepLastNRounds">Number of tool rounds to keep (default: 5).</param>
    /// <returns>True if any messages were removed, false otherwise.</returns>
    public static bool TrimToolMessages(JsonArray messages, int keepLastNRounds = 5)
    {
        if (messages == null || messages.Count == 0) return false;

        var toolRounds = IdentifyToolRounds(messages);
        if (toolRounds.Count <= keepLastNRounds) return false;

        var roundsToRemove = toolRounds.Count - keepLastNRounds;
        var indicesToRemove = new HashSet<int>();

        for (int i = 0; i < roundsToRemove; i++)
        {
            foreach (var idx in toolRounds[i].Indices)
            {
                indicesToRemove.Add(idx);
            }
        }

        if (indicesToRemove.Count == 0) return false;

        // Remove in reverse order to preserve indices
        foreach (var idx in indicesToRemove.OrderByDescending(x => x))
        {
            messages.RemoveAt(idx);
        }

        return true;
    }

    #endregion

    #region Layer 2: Thinking Compression

    /// <summary>
    /// Layer 2: Compress thinking content while preserving signatures.
    /// Breaks Prompt Cache but keeps signature chain intact.
    /// </summary>
    /// <param name="messages">The messages array to modify.</param>
    /// <param name="protectedLastN">Number of messages at the end to protect from compression (default: 4).</param>
    /// <returns>True if any thinking blocks were compressed, false otherwise.</returns>
    public static bool CompressThinkingPreserveSignature(JsonArray messages, int protectedLastN = 4)
    {
        if (messages == null || messages.Count == 0) return false;

        int startProtection = Math.Max(0, messages.Count - protectedLastN);
        bool modified = false;

        for (int i = 0; i < startProtection; i++)
        {
            var msg = messages[i];
            var role = JsonHelper.GetString(msg, "role");
            if (role != "assistant") continue;

            var content = JsonHelper.GetPath(msg, "content") as JsonArray;
            if (content == null) continue;

            foreach (var block in content)
            {
                var type = JsonHelper.GetString(block, "type");
                if (type != "thinking") continue;

                var signature = JsonHelper.GetString(block, "signature");
                var thinking = JsonHelper.GetString(block, "thinking");

                // Only compress if signature exists and thinking has content
                if (!string.IsNullOrEmpty(signature) && thinking?.Length > 10)
                {
                    (block as JsonObject)!["thinking"] = "...";
                    modified = true;
                }
            }
        }

        return modified;
    }

    #endregion

    #region Layer 3: Signature Extraction

    /// <summary>
    /// Layer 3 Helper: Extract last valid signature for fork operations.
    /// Scans from the end of messages to find the most recent valid signature.
    /// </summary>
    /// <param name="messages">The messages array to scan.</param>
    /// <returns>The last valid signature (>= 50 chars) or null if none found.</returns>
    public static string? ExtractLastValidSignature(JsonArray messages)
    {
        if (messages == null || messages.Count == 0) return null;

        for (int i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            var role = JsonHelper.GetString(msg, "role");
            if (role != "assistant") continue;

            var content = JsonHelper.GetPath(msg, "content") as JsonArray;
            if (content == null) continue;

            // Scan content blocks in reverse order within the message
            for (int j = content.Count - 1; j >= 0; j--)
            {
                var block = content[j];
                var type = JsonHelper.GetString(block, "type");
                if (type != "thinking") continue;

                var signature = JsonHelper.GetString(block, "signature");
                if (signature?.Length >= 50)
                    return signature;
            }
        }

        return null;
    }

    #endregion

    #region Progressive Compression

    /// <summary>
    /// Result of progressive compression operation.
    /// </summary>
    public class CompressionResult
    {
        /// <summary>
        /// Whether any compression was applied.
        /// </summary>
        public bool WasCompressed { get; set; }

        /// <summary>
        /// The final context pressure percentage after compression.
        /// </summary>
        public int FinalPressure { get; set; }

        /// <summary>
        /// The last valid signature extracted (if Layer 3 was triggered).
        /// </summary>
        public string? LastSignature { get; set; }

        /// <summary>
        /// Which layers were applied (1, 2, and/or 3).
        /// </summary>
        public List<int> LayersApplied { get; } = new();
    }

    /// <summary>
    /// Apply progressive compression based on context pressure.
    /// Layer 1 (60%+): Trim old tool messages
    /// Layer 2 (75%+): Compress thinking content
    /// Layer 3 (90%+): Extract last signature for potential fork
    /// </summary>
    /// <param name="request">The JSON request object.</param>
    /// <param name="maxTokens">Maximum token limit for the model.</param>
    /// <param name="layer1Threshold">Pressure threshold for Layer 1 (default: 60%).</param>
    /// <param name="layer2Threshold">Pressure threshold for Layer 2 (default: 75%).</param>
    /// <param name="layer3Threshold">Pressure threshold for Layer 3 (default: 90%).</param>
    /// <returns>Compression result with details about what was applied.</returns>
    public static CompressionResult ApplyProgressiveCompression(
        JsonNode request,
        int maxTokens,
        int layer1Threshold = 60,
        int layer2Threshold = 75,
        int layer3Threshold = 90)
    {
        var result = new CompressionResult();

        if (request == null || maxTokens <= 0)
        {
            return result;
        }

        var messages = JsonHelper.GetPath(request, "messages") as JsonArray;
        if (messages == null || messages.Count == 0)
        {
            return result;
        }

        var estimatedTokens = EstimateRequestTokens(request);
        var pressure = CalculatePressure(estimatedTokens, maxTokens);
        result.FinalPressure = pressure;

        // Layer 1: Tool message trimming (60%+)
        if (pressure >= layer1Threshold)
        {
            if (TrimToolMessages(messages, keepLastNRounds: 5))
            {
                result.WasCompressed = true;
                result.LayersApplied.Add(1);
            }
            pressure = RecalculatePressure(request, maxTokens);
            result.FinalPressure = pressure;
        }

        // Layer 2: Thinking compression (75%+)
        if (pressure >= layer2Threshold)
        {
            if (CompressThinkingPreserveSignature(messages, protectedLastN: 4))
            {
                result.WasCompressed = true;
                result.LayersApplied.Add(2);
            }
            pressure = RecalculatePressure(request, maxTokens);
            result.FinalPressure = pressure;
        }

        // Layer 3: Extract signature for potential fork (90%+)
        if (pressure >= layer3Threshold)
        {
            result.LastSignature = ExtractLastValidSignature(messages);
            result.LayersApplied.Add(3);
        }

        return result;
    }

    /// <summary>
    /// Calculate context pressure as a percentage.
    /// </summary>
    private static int CalculatePressure(int estimatedTokens, int maxTokens)
    {
        if (maxTokens <= 0) return 0;
        return (int)(estimatedTokens * 100.0 / maxTokens);
    }

    /// <summary>
    /// Recalculate pressure after compression.
    /// </summary>
    private static int RecalculatePressure(JsonNode request, int maxTokens)
    {
        var estimatedTokens = EstimateRequestTokens(request);
        return CalculatePressure(estimatedTokens, maxTokens);
    }

    #endregion
}
