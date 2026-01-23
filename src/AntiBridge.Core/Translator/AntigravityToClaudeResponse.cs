using System.Text;
using System.Text.Json.Nodes;
using AntiBridge.Core.Services;

namespace AntiBridge.Core.Translator;

/// <summary>
/// State for streaming response conversion
/// </summary>
public class ClaudeStreamState
{
    public bool HasFirstResponse { get; set; }
    public int ResponseType { get; set; } // 0=none, 1=content, 2=thinking, 3=function
    public int ResponseIndex { get; set; }
    public bool HasFinishReason { get; set; }
    public string FinishReason { get; set; } = "";
    public bool HasUsageMetadata { get; set; }
    public long PromptTokenCount { get; set; }
    public long CandidatesTokenCount { get; set; }
    public long ThoughtsTokenCount { get; set; }
    public long TotalTokenCount { get; set; }
    public long CachedTokenCount { get; set; }
    public bool HasSentFinalEvents { get; set; }
    public bool HasToolUse { get; set; }
    public bool HasContent { get; set; }
    public StringBuilder CurrentThinkingText { get; } = new();
    
    /// <summary>
    /// Pending thinking text to be cached when signature is received
    /// </summary>
    public string? PendingThinkingText { get; set; }
}

/// <summary>
/// Converts Antigravity API responses to Claude API format
/// Ported from CLIProxyAPI/internal/translator/antigravity/claude/antigravity_claude_response.go
/// </summary>
public static class AntigravityToClaudeResponse
{
    private static long _toolUseIdCounter;

    #region Response Processing Fixes (Task 10.3)

    /// <summary>
    /// Decodes a Base64-encoded signature to UTF-8 string.
    /// Gemini sends Base64 encoded signatures, but Claude expects raw strings.
    /// Ported from Rust response.rs: process_part() signature handling
    /// </summary>
    /// <param name="signature">The potentially Base64-encoded signature</param>
    /// <returns>The decoded signature, or the original if not Base64</returns>
    public static string DecodeBase64Signature(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return signature ?? "";

        try
        {
            // Try to decode as Base64
            var decodedBytes = Convert.FromBase64String(signature);
            var decodedString = Encoding.UTF8.GetString(decodedBytes);
            
            // Verify it's valid UTF-8 text (not binary garbage)
            // If the decoded string contains mostly printable characters, it's likely valid
            if (IsPrintableString(decodedString))
            {
                return decodedString;
            }
            
            // Not valid UTF-8 text, return original
            return signature;
        }
        catch
        {
            // Not valid Base64, return original
            return signature;
        }
    }

    /// <summary>
    /// Checks if a string contains mostly printable characters.
    /// </summary>
    private static bool IsPrintableString(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        
        int printableCount = 0;
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsWhiteSpace(c) || char.IsSymbol(c))
            {
                printableCount++;
            }
        }
        
        // Consider it printable if at least 80% of characters are printable
        return (double)printableCount / s.Length >= 0.8;
    }

    /// <summary>
    /// Remaps function call arguments for Gemini → Claude compatibility.
    /// Gemini sometimes uses different parameter names than specified in tool schema.
    /// Ported from Rust: remap_function_call_args()
    /// </summary>
    /// <param name="toolName">The name of the tool being called</param>
    /// <param name="args">The arguments object to remap (modified in place)</param>
    public static void RemapFunctionCallArgs(string toolName, JsonObject? args)
    {
        if (args == null || string.IsNullOrEmpty(toolName))
            return;

        var toolNameLower = toolName.ToLowerInvariant();

        switch (toolNameLower)
        {
            case "grep":
            case "search":
            case "search_code_definitions":
            case "search_code_snippets":
                // Gemini hallucination: maps parameter description to "description" field
                if (args.ContainsKey("description") && !args.ContainsKey("pattern"))
                {
                    args["pattern"] = args["description"]?.DeepClone();
                    args.Remove("description");
                }

                // Gemini uses "query", Claude Code expects "pattern"
                if (args.ContainsKey("query") && !args.ContainsKey("pattern"))
                {
                    args["pattern"] = args["query"]?.DeepClone();
                    args.Remove("query");
                }

                // Claude Code uses "path" (string), NOT "paths" (array)
                RemapPathsToPath(args);
                break;

            case "glob":
                // Same remapping as grep
                if (args.ContainsKey("description") && !args.ContainsKey("pattern"))
                {
                    args["pattern"] = args["description"]?.DeepClone();
                    args.Remove("description");
                }

                if (args.ContainsKey("query") && !args.ContainsKey("pattern"))
                {
                    args["pattern"] = args["query"]?.DeepClone();
                    args.Remove("query");
                }

                RemapPathsToPath(args);
                break;

            case "read":
                // Gemini might use "path" vs "file_path"
                if (args.ContainsKey("path") && !args.ContainsKey("file_path"))
                {
                    args["file_path"] = args["path"]?.DeepClone();
                    args.Remove("path");
                }
                break;

            case "ls":
                // LS tool: ensure "path" parameter exists
                if (!args.ContainsKey("path"))
                {
                    args["path"] = ".";
                }
                break;

            case "enterplanmode":
                // EnterPlanMode tool: clear all args
                var keys = args.Select(kvp => kvp.Key).ToList();
                foreach (var key in keys)
                {
                    args.Remove(key);
                }
                break;

            default:
                // Generic property mapping for all tools
                // If a tool has "paths" (array of 1) but no "path", convert it
                RemapPathsToPath(args);
                break;
        }
    }

    /// <summary>
    /// Remaps "paths" array to "path" string, taking the first element.
    /// Adds default path "." if neither exists.
    /// </summary>
    private static void RemapPathsToPath(JsonObject args)
    {
        if (args.ContainsKey("path"))
            return;

        if (args.ContainsKey("paths"))
        {
            var paths = args["paths"];
            string pathStr = ".";

            if (paths is JsonArray pathsArray && pathsArray.Count > 0)
            {
                var firstPath = pathsArray[0];
                if (firstPath is JsonValue pathVal)
                {
                    try
                    {
                        pathStr = pathVal.GetValue<string>();
                    }
                    catch { }
                }
            }
            else if (paths is JsonValue pVal)
            {
                try
                {
                    pathStr = pVal.GetValue<string>();
                }
                catch { }
            }

            args["path"] = pathStr;
            args.Remove("paths");
        }
        else
        {
            // Default to current directory if missing
            args["path"] = ".";
        }
    }

    #endregion

    /// <summary>
    /// Convert streaming response chunk
    /// </summary>
    /// <param name="modelName">The model name</param>
    /// <param name="rawJson">Raw JSON response bytes</param>
    /// <param name="state">Streaming state</param>
    /// <param name="signatureCache">Optional signature cache to store thinking signatures</param>
    public static List<string> ConvertStream(string modelName, byte[] rawJson, ClaudeStreamState state, ISignatureCache? signatureCache = null)
    {
        var results = new List<string>();
        
        // Handle [DONE] marker
        if (rawJson.SequenceEqual("[DONE]"u8.ToArray()))
        {
            if (state.HasContent)
            {
                var output = new StringBuilder();
                AppendFinalEvents(state, output, true);
                output.Append("event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n\n");
                results.Add(output.ToString());
            }
            return results;
        }

        var root = JsonHelper.Parse(rawJson);
        if (root == null) return results;

        var output2 = new StringBuilder();

        // Initialize streaming session
        if (!state.HasFirstResponse)
        {
            output2.Append("event: message_start\n");
            
            var messageStart = new JsonObject
            {
                ["type"] = "message_start",
                ["message"] = new JsonObject
                {
                    ["id"] = JsonHelper.GetString(root, "response.responseId") ?? $"msg_{Guid.NewGuid():N}",
                    ["type"] = "message",
                    ["role"] = "assistant",
                    ["content"] = new JsonArray(),
                    ["model"] = JsonHelper.GetString(root, "response.modelVersion") ?? modelName,
                    ["stop_reason"] = (JsonNode?)null,
                    ["stop_sequence"] = (JsonNode?)null,
                    ["usage"] = new JsonObject
                    {
                        ["input_tokens"] = JsonHelper.GetLong(root, "response.cpaUsageMetadata.promptTokenCount") ?? 0,
                        ["output_tokens"] = JsonHelper.GetLong(root, "response.cpaUsageMetadata.candidatesTokenCount") ?? 0
                    }
                }
            };
            
            output2.Append($"data: {JsonHelper.Stringify(messageStart)}\n\n\n");
            state.HasFirstResponse = true;
        }

        // Process parts
        var parts = JsonHelper.GetPath(root, "response.candidates.0.content.parts") as JsonArray;
        if (parts != null)
        {
            foreach (var part in parts)
            {
                var textResult = JsonHelper.GetPath(part, "text");
                var functionCall = JsonHelper.GetPath(part, "functionCall");
                var isThought = JsonHelper.GetBool(part, "thought") ?? false;

                if (textResult != null)
                {
                    var text = textResult.GetValue<string>();
                    
                    if (isThought)
                    {
                        // Handle thinking content
                        var thoughtSig = JsonHelper.GetString(part, "thoughtSignature") 
                                      ?? JsonHelper.GetString(part, "thought_signature");

                        if (!string.IsNullOrEmpty(thoughtSig))
                        {
                            // Decode Base64 signature if needed (Task 10.3.1)
                            var decodedSig = DecodeBase64Signature(thoughtSig);
                            
                            // Store thinking text and signature in cache
                            if (signatureCache != null && state.CurrentThinkingText.Length > 0)
                            {
                                var thinkingText = state.CurrentThinkingText.ToString();
                                signatureCache.SetSignature(thinkingText, decodedSig);
                            }
                            
                            // Signature delta
                            output2.Append("event: content_block_delta\n");
                            var sigDelta = new JsonObject
                            {
                                ["type"] = "content_block_delta",
                                ["index"] = state.ResponseIndex,
                                ["delta"] = new JsonObject
                                {
                                    ["type"] = "signature_delta",
                                    ["signature"] = $"{GetModelGroup(modelName)}#{decodedSig}"
                                }
                            };
                            output2.Append($"data: {JsonHelper.Stringify(sigDelta)}\n\n\n");
                            state.HasContent = true;
                            state.CurrentThinkingText.Clear();
                        }
                        else if (state.ResponseType == 2)
                        {
                            // Continue thinking block
                            state.CurrentThinkingText.Append(text);
                            output2.Append("event: content_block_delta\n");
                            var thinkDelta = new JsonObject
                            {
                                ["type"] = "content_block_delta",
                                ["index"] = state.ResponseIndex,
                                ["delta"] = new JsonObject
                                {
                                    ["type"] = "thinking_delta",
                                    ["thinking"] = text
                                }
                            };
                            output2.Append($"data: {JsonHelper.Stringify(thinkDelta)}\n\n\n");
                            state.HasContent = true;
                        }
                        else
                        {
                            // Start new thinking block
                            if (state.ResponseType != 0)
                            {
                                output2.Append("event: content_block_stop\n");
                                output2.Append($"data: {{\"type\":\"content_block_stop\",\"index\":{state.ResponseIndex}}}\n\n\n");
                                state.ResponseIndex++;
                            }

                            output2.Append("event: content_block_start\n");
                            output2.Append($"data: {{\"type\":\"content_block_start\",\"index\":{state.ResponseIndex},\"content_block\":{{\"type\":\"thinking\",\"thinking\":\"\"}}}}\n\n\n");
                            
                            output2.Append("event: content_block_delta\n");
                            var thinkDelta = new JsonObject
                            {
                                ["type"] = "content_block_delta",
                                ["index"] = state.ResponseIndex,
                                ["delta"] = new JsonObject
                                {
                                    ["type"] = "thinking_delta",
                                    ["thinking"] = text
                                }
                            };
                            output2.Append($"data: {JsonHelper.Stringify(thinkDelta)}\n\n\n");
                            
                            state.ResponseType = 2;
                            state.HasContent = true;
                            state.CurrentThinkingText.Clear();
                            state.CurrentThinkingText.Append(text);
                        }
                    }
                    else
                    {
                        // Regular text content
                        var finishReason = JsonHelper.GetString(root, "response.candidates.0.finishReason");
                        if (!string.IsNullOrEmpty(text) || string.IsNullOrEmpty(finishReason))
                        {
                            if (state.ResponseType == 1)
                            {
                                // Continue text block
                                output2.Append("event: content_block_delta\n");
                                var textDelta = new JsonObject
                                {
                                    ["type"] = "content_block_delta",
                                    ["index"] = state.ResponseIndex,
                                    ["delta"] = new JsonObject
                                    {
                                        ["type"] = "text_delta",
                                        ["text"] = text
                                    }
                                };
                                output2.Append($"data: {JsonHelper.Stringify(textDelta)}\n\n\n");
                                state.HasContent = true;
                            }
                            else if (!string.IsNullOrEmpty(text))
                            {
                                // Start new text block
                                if (state.ResponseType != 0)
                                {
                                    output2.Append("event: content_block_stop\n");
                                    output2.Append($"data: {{\"type\":\"content_block_stop\",\"index\":{state.ResponseIndex}}}\n\n\n");
                                    state.ResponseIndex++;
                                }

                                output2.Append("event: content_block_start\n");
                                output2.Append($"data: {{\"type\":\"content_block_start\",\"index\":{state.ResponseIndex},\"content_block\":{{\"type\":\"text\",\"text\":\"\"}}}}\n\n\n");
                                
                                output2.Append("event: content_block_delta\n");
                                var textDelta = new JsonObject
                                {
                                    ["type"] = "content_block_delta",
                                    ["index"] = state.ResponseIndex,
                                    ["delta"] = new JsonObject
                                    {
                                        ["type"] = "text_delta",
                                        ["text"] = text
                                    }
                                };
                                output2.Append($"data: {JsonHelper.Stringify(textDelta)}\n\n\n");
                                
                                state.ResponseType = 1;
                                state.HasContent = true;
                            }
                        }
                    }
                }
                else if (functionCall != null)
                {
                    // Handle function call
                    state.HasToolUse = true;
                    var fcName = JsonHelper.GetString(functionCall, "name") ?? "";

                    // Close previous block
                    if (state.ResponseType != 0)
                    {
                        output2.Append("event: content_block_stop\n");
                        output2.Append($"data: {{\"type\":\"content_block_stop\",\"index\":{state.ResponseIndex}}}\n\n\n");
                        state.ResponseIndex++;
                    }

                    // Start tool use block
                    var toolId = $"{fcName}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Interlocked.Increment(ref _toolUseIdCounter)}";
                    
                    output2.Append("event: content_block_start\n");
                    var toolStart = new JsonObject
                    {
                        ["type"] = "content_block_start",
                        ["index"] = state.ResponseIndex,
                        ["content_block"] = new JsonObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = toolId,
                            ["name"] = fcName,
                            ["input"] = new JsonObject()
                        }
                    };
                    output2.Append($"data: {JsonHelper.Stringify(toolStart)}\n\n\n");

                    var fcArgs = JsonHelper.GetPath(functionCall, "args")?.DeepClone() as JsonObject;
                    if (fcArgs != null)
                    {
                        // Remap function call arguments (Task 10.3)
                        RemapFunctionCallArgs(fcName, fcArgs);
                        
                        output2.Append("event: content_block_delta\n");
                        var argsDelta = new JsonObject
                        {
                            ["type"] = "content_block_delta",
                            ["index"] = state.ResponseIndex,
                            ["delta"] = new JsonObject
                            {
                                ["type"] = "input_json_delta",
                                ["partial_json"] = JsonHelper.Stringify(fcArgs)
                            }
                        };
                        output2.Append($"data: {JsonHelper.Stringify(argsDelta)}\n\n\n");
                    }

                    state.ResponseType = 3;
                    state.HasContent = true;
                }
            }
        }

        // Check finish reason
        var finishReasonResult = JsonHelper.GetString(root, "response.candidates.0.finishReason");
        if (!string.IsNullOrEmpty(finishReasonResult))
        {
            state.HasFinishReason = true;
            state.FinishReason = finishReasonResult;
        }

        // Check usage metadata
        var usageResult = JsonHelper.GetPath(root, "response.usageMetadata");
        if (usageResult != null)
        {
            state.HasUsageMetadata = true;
            state.CachedTokenCount = JsonHelper.GetLong(usageResult, "cachedContentTokenCount") ?? 0;
            state.PromptTokenCount = (JsonHelper.GetLong(usageResult, "promptTokenCount") ?? 0) - state.CachedTokenCount;
            state.CandidatesTokenCount = JsonHelper.GetLong(usageResult, "candidatesTokenCount") ?? 0;
            state.ThoughtsTokenCount = JsonHelper.GetLong(usageResult, "thoughtsTokenCount") ?? 0;
            state.TotalTokenCount = JsonHelper.GetLong(usageResult, "totalTokenCount") ?? 0;
            
            if (state.CandidatesTokenCount == 0 && state.TotalTokenCount > 0)
            {
                state.CandidatesTokenCount = state.TotalTokenCount - state.PromptTokenCount - state.ThoughtsTokenCount;
                if (state.CandidatesTokenCount < 0) state.CandidatesTokenCount = 0;
            }
        }

        // Send final events if ready
        if (state.HasUsageMetadata && state.HasFinishReason)
        {
            AppendFinalEvents(state, output2, false);
        }

        if (output2.Length > 0)
        {
            results.Add(output2.ToString());
        }

        return results;
    }

    private static void AppendFinalEvents(ClaudeStreamState state, StringBuilder output, bool force)
    {
        if (state.HasSentFinalEvents) return;
        if (!state.HasUsageMetadata && !force) return;
        if (!state.HasContent) return;

        if (state.ResponseType != 0)
        {
            output.Append("event: content_block_stop\n");
            output.Append($"data: {{\"type\":\"content_block_stop\",\"index\":{state.ResponseIndex}}}\n\n\n");
            state.ResponseType = 0;
        }

        var stopReason = ResolveStopReason(state);
        var usageOutputTokens = state.CandidatesTokenCount + state.ThoughtsTokenCount;
        if (usageOutputTokens == 0 && state.TotalTokenCount > 0)
        {
            usageOutputTokens = state.TotalTokenCount - state.PromptTokenCount;
            if (usageOutputTokens < 0) usageOutputTokens = 0;
        }

        var messageDelta = new JsonObject
        {
            ["type"] = "message_delta",
            ["delta"] = new JsonObject
            {
                ["stop_reason"] = stopReason,
                ["stop_sequence"] = (JsonNode?)null
            },
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = state.PromptTokenCount,
                ["output_tokens"] = usageOutputTokens
            }
        };

        if (state.CachedTokenCount > 0)
        {
            (messageDelta["usage"] as JsonObject)!["cache_read_input_tokens"] = state.CachedTokenCount;
        }

        output.Append("event: message_delta\n");
        output.Append($"data: {JsonHelper.Stringify(messageDelta)}\n\n\n");

        state.HasSentFinalEvents = true;
    }

    private static string ResolveStopReason(ClaudeStreamState state)
    {
        if (state.HasToolUse) return "tool_use";

        return state.FinishReason switch
        {
            "MAX_TOKENS" => "max_tokens",
            "STOP" or "FINISH_REASON_UNSPECIFIED" or "UNKNOWN" => "end_turn",
            _ => "end_turn"
        };
    }

    /// <summary>
    /// Convert non-streaming response
    /// </summary>
    /// <param name="modelName">The model name</param>
    /// <param name="rawJson">Raw JSON response bytes</param>
    /// <param name="signatureCache">Optional signature cache to store thinking signatures</param>
    public static string ConvertNonStream(string modelName, byte[] rawJson, ISignatureCache? signatureCache = null)
    {
        var root = JsonHelper.Parse(rawJson);
        if (root == null) return "{}";

        var promptTokens = JsonHelper.GetLong(root, "response.usageMetadata.promptTokenCount") ?? 0;
        var candidateTokens = JsonHelper.GetLong(root, "response.usageMetadata.candidatesTokenCount") ?? 0;
        var thoughtTokens = JsonHelper.GetLong(root, "response.usageMetadata.thoughtsTokenCount") ?? 0;
        var totalTokens = JsonHelper.GetLong(root, "response.usageMetadata.totalTokenCount") ?? 0;
        var cachedTokens = JsonHelper.GetLong(root, "response.usageMetadata.cachedContentTokenCount") ?? 0;
        
        var outputTokens = candidateTokens + thoughtTokens;
        if (outputTokens == 0 && totalTokens > 0)
        {
            outputTokens = totalTokens - promptTokens;
            if (outputTokens < 0) outputTokens = 0;
        }

        var response = new JsonObject
        {
            ["id"] = JsonHelper.GetString(root, "response.responseId") ?? $"msg_{Guid.NewGuid():N}",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = JsonHelper.GetString(root, "response.modelVersion") ?? modelName,
            ["content"] = new JsonArray(),
            ["stop_reason"] = (JsonNode?)null,
            ["stop_sequence"] = (JsonNode?)null,
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = promptTokens - cachedTokens,
                ["output_tokens"] = outputTokens
            }
        };

        if (cachedTokens > 0)
        {
            (response["usage"] as JsonObject)!["cache_read_input_tokens"] = cachedTokens;
        }

        var content = response["content"] as JsonArray;
        var parts = JsonHelper.GetPath(root, "response.candidates.0.content.parts") as JsonArray;
        var hasToolCall = false;
        var toolIdCounter = 0;

        var textBuilder = new StringBuilder();
        var thinkingBuilder = new StringBuilder();
        var thinkingSignature = "";

        void FlushText()
        {
            if (textBuilder.Length == 0) return;
            content!.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = textBuilder.ToString()
            });
            textBuilder.Clear();
        }

        void FlushThinking()
        {
            if (thinkingBuilder.Length == 0 && string.IsNullOrEmpty(thinkingSignature)) return;
            
            // Store thinking text and signature in cache
            if (signatureCache != null && thinkingBuilder.Length > 0 && !string.IsNullOrEmpty(thinkingSignature))
            {
                signatureCache.SetSignature(thinkingBuilder.ToString(), thinkingSignature);
            }
            
            var block = new JsonObject
            {
                ["type"] = "thinking",
                ["thinking"] = thinkingBuilder.ToString()
            };
            if (!string.IsNullOrEmpty(thinkingSignature))
            {
                block["signature"] = $"{GetModelGroup(modelName)}#{thinkingSignature}";
            }
            content!.Add(block);
            thinkingBuilder.Clear();
            thinkingSignature = "";
        }

        if (parts != null)
        {
            foreach (var part in parts)
            {
                var isThought = JsonHelper.GetBool(part, "thought") ?? false;
                
                if (isThought)
                {
                    var sig = JsonHelper.GetString(part, "thoughtSignature") 
                           ?? JsonHelper.GetString(part, "thought_signature");
                    if (!string.IsNullOrEmpty(sig))
                    {
                        // Decode Base64 signature if needed (Task 10.3.1)
                        thinkingSignature = DecodeBase64Signature(sig);
                    }
                }

                var text = JsonHelper.GetString(part, "text");
                if (!string.IsNullOrEmpty(text))
                {
                    if (isThought)
                    {
                        FlushText();
                        thinkingBuilder.Append(text);
                    }
                    else
                    {
                        FlushThinking();
                        textBuilder.Append(text);
                    }
                    continue;
                }

                var functionCall = JsonHelper.GetPath(part, "functionCall");
                if (functionCall != null)
                {
                    FlushThinking();
                    FlushText();
                    hasToolCall = true;

                    var name = JsonHelper.GetString(functionCall, "name") ?? "";
                    toolIdCounter++;
                    
                    // Get and remap function call arguments (Task 10.3)
                    var argsNode = JsonHelper.GetPath(functionCall, "args")?.DeepClone() as JsonObject ?? new JsonObject();
                    RemapFunctionCallArgs(name, argsNode);
                    
                    var toolBlock = new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = $"tool_{toolIdCounter}",
                        ["name"] = name,
                        ["input"] = argsNode
                    };
                    content!.Add(toolBlock);
                }
            }
        }

        FlushThinking();
        FlushText();

        // Set stop reason
        var stopReason = "end_turn";
        if (hasToolCall)
        {
            stopReason = "tool_use";
        }
        else
        {
            var finish = JsonHelper.GetString(root, "response.candidates.0.finishReason");
            stopReason = finish switch
            {
                "MAX_TOKENS" => "max_tokens",
                _ => "end_turn"
            };
        }
        response["stop_reason"] = stopReason;

        return JsonHelper.Stringify(response);
    }

    private static string GetModelGroup(string modelName)
    {
        if (modelName.Contains("claude")) return "claude";
        if (modelName.Contains("gemini")) return "gemini";
        return "antigravity";
    }
}
