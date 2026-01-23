using System.Text;
using System.Text.Json.Nodes;
using AntiBridge.Core.Services;

namespace AntiBridge.Core.Translator;

/// <summary>
/// State for OpenAI streaming response conversion
/// </summary>
public class OpenAIStreamState
{
    public long UnixTimestamp { get; set; }
    public int FunctionIndex { get; set; }
    
    /// <summary>
    /// Counter for generating unique tool call IDs within a session.
    /// </summary>
    public long ToolCallIdCounter { get; set; }
    
    /// <summary>
    /// Accumulated reasoning content for the current response.
    /// </summary>
    public StringBuilder ReasoningContent { get; } = new();
}

/// <summary>
/// Converts Antigravity API responses to OpenAI API format
/// Ported from Antigravity-Manager/src-tauri/src/proxy/mappers/openai/response.rs
/// </summary>
public static class AntigravityToOpenAIResponse
{
    /// <summary>
    /// Global counter for generating unique tool call IDs across sessions.
    /// </summary>
    private static long _globalToolCallIdCounter;

    #region Task 11.5: Response Processing Fixes

    /// <summary>
    /// Decodes a Base64-encoded signature to UTF-8 string.
    /// Task 11.5.3: Implement DecodeBase64Signature().
    /// Ported from Claude translator.
    /// </summary>
    public static string DecodeBase64Signature(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return signature ?? "";

        try
        {
            var decodedBytes = Convert.FromBase64String(signature);
            var decodedString = Encoding.UTF8.GetString(decodedBytes);
            
            // Verify it's valid UTF-8 text
            if (IsPrintableString(decodedString))
            {
                return decodedString;
            }
            
            return signature;
        }
        catch
        {
            return signature;
        }
    }

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
        
        return (double)printableCount / s.Length >= 0.8;
    }

    /// <summary>
    /// Remaps function call arguments for Gemini → OpenAI compatibility.
    /// Task 11.5.4: Implement RemapFunctionCallArgs().
    /// Same as Claude translator.
    /// </summary>
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

            case "glob":
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
                if (args.ContainsKey("path") && !args.ContainsKey("file_path"))
                {
                    args["file_path"] = args["path"]?.DeepClone();
                    args.Remove("path");
                }
                break;

            case "ls":
                if (!args.ContainsKey("path"))
                {
                    args["path"] = ".";
                }
                break;

            case "enterplanmode":
                var keys = args.Select(kvp => kvp.Key).ToList();
                foreach (var key in keys)
                {
                    args.Remove(key);
                }
                break;

            default:
                RemapPathsToPath(args);
                break;
        }
    }

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
                    try { pathStr = pathVal.GetValue<string>(); }
                    catch { }
                }
            }
            else if (paths is JsonValue pVal)
            {
                try { pathStr = pVal.GetValue<string>(); }
                catch { }
            }

            args["path"] = pathStr;
            args.Remove("paths");
        }
        else
        {
            args["path"] = ".";
        }
    }

    /// <summary>
    /// Formats grounding metadata (web search results) as markdown citations.
    /// Task 11.5.2: Implement grounding metadata handling.
    /// Ported from Rust: groundingMetadata handling
    /// </summary>
    public static string FormatGroundingMetadata(JsonNode? grounding)
    {
        if (grounding == null) return "";

        var result = new StringBuilder();

        // Process search queries
        var queries = JsonHelper.GetPath(grounding, "webSearchQueries") as JsonArray;
        if (queries != null && queries.Count > 0)
        {
            var queryList = queries
                .Select(q => q?.GetValue<string>())
                .Where(q => !string.IsNullOrEmpty(q))
                .ToList();
            
            if (queryList.Count > 0)
            {
                result.Append("\n\n---\n**🔍 Searched:** ");
                result.Append(string.Join(", ", queryList));
            }
        }

        // Process grounding chunks (citations)
        var chunks = JsonHelper.GetPath(grounding, "groundingChunks") as JsonArray;
        if (chunks != null && chunks.Count > 0)
        {
            var links = new List<string>();
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var web = JsonHelper.GetPath(chunk, "web");
                if (web != null)
                {
                    var title = JsonHelper.GetString(web, "title") ?? "Source";
                    var uri = JsonHelper.GetString(web, "uri") ?? "#";
                    links.Add($"[{i + 1}] [{title}]({uri})");
                }
            }

            if (links.Count > 0)
            {
                result.Append("\n\n**🌐 Sources:**\n");
                result.Append(string.Join("\n", links));
            }
        }

        return result.ToString();
    }

    #endregion

    /// <summary>
    /// Convert streaming response chunk.
    /// </summary>
    public static List<string> ConvertStream(string modelName, byte[] rawJson, OpenAIStreamState state, ISignatureCache? signatureCache = null)
    {
        var results = new List<string>();

        // Handle [DONE] marker
        if (rawJson.SequenceEqual("[DONE]"u8.ToArray()))
        {
            return results;
        }

        var root = JsonHelper.Parse(rawJson);
        if (root == null) return results;

        // Build template
        var template = new JsonObject
        {
            ["id"] = "",
            ["object"] = "chat.completion.chunk",
            ["created"] = state.UnixTimestamp,
            ["model"] = modelName,
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["index"] = 0,
                    ["delta"] = new JsonObject(),
                    ["finish_reason"] = (JsonNode?)null,
                    ["native_finish_reason"] = (JsonNode?)null
                }
            }
        };

        // Model version
        var modelVersion = JsonHelper.GetString(root, "response.modelVersion");
        if (!string.IsNullOrEmpty(modelVersion))
        {
            template["model"] = modelVersion;
        }

        // Creation time
        var createTime = JsonHelper.GetString(root, "response.createTime");
        if (!string.IsNullOrEmpty(createTime) && DateTime.TryParse(createTime, out var dt))
        {
            state.UnixTimestamp = new DateTimeOffset(dt).ToUnixTimeSeconds();
            template["created"] = state.UnixTimestamp;
        }

        // Response ID
        var responseId = JsonHelper.GetString(root, "response.responseId");
        if (!string.IsNullOrEmpty(responseId))
        {
            template["id"] = responseId;
        }

        // Finish reason
        var finishReason = JsonHelper.GetString(root, "response.candidates.0.finishReason");
        if (!string.IsNullOrEmpty(finishReason))
        {
            var choice = (template["choices"] as JsonArray)?[0] as JsonObject;
            choice!["finish_reason"] = finishReason.ToLower();
            choice["native_finish_reason"] = finishReason.ToLower();
        }

        // Usage metadata
        var usage = JsonHelper.GetPath(root, "response.usageMetadata");
        if (usage != null)
        {
            var cachedTokens = JsonHelper.GetLong(usage, "cachedContentTokenCount") ?? 0;
            var promptTokens = JsonHelper.GetLong(usage, "promptTokenCount") ?? 0;
            var thoughtsTokens = JsonHelper.GetLong(usage, "thoughtsTokenCount") ?? 0;
            var candidatesTokens = JsonHelper.GetLong(usage, "candidatesTokenCount") ?? 0;
            var totalTokens = JsonHelper.GetLong(usage, "totalTokenCount") ?? 0;

            var effectivePromptTokens = promptTokens - cachedTokens;
            if (effectivePromptTokens < 0) effectivePromptTokens = 0;

            var usageObj = new JsonObject
            {
                ["prompt_tokens"] = effectivePromptTokens,
                ["completion_tokens"] = candidatesTokens + thoughtsTokens,
                ["total_tokens"] = totalTokens
            };

            if (thoughtsTokens > 0)
            {
                usageObj["completion_tokens_details"] = new JsonObject
                {
                    ["reasoning_tokens"] = thoughtsTokens
                };
            }

            if (cachedTokens > 0)
            {
                usageObj["prompt_tokens_details"] = new JsonObject
                {
                    ["cached_tokens"] = cachedTokens
                };
            }

            template["usage"] = usageObj;
        }

        // Process parts
        var parts = JsonHelper.GetPath(root, "response.candidates.0.content.parts") as JsonArray;
        var hasFunctionCall = false;
        var delta = ((template["choices"] as JsonArray)?[0] as JsonObject)?["delta"] as JsonObject;

        if (parts != null && delta != null)
        {
            foreach (var part in parts)
            {
                // Task 11.5.1: Capture and store thought signature
                var thoughtSig = JsonHelper.GetString(part, "thoughtSignature") 
                              ?? JsonHelper.GetString(part, "thought_signature");
                if (!string.IsNullOrEmpty(thoughtSig))
                {
                    var decodedSig = DecodeBase64Signature(thoughtSig);
                    OpenAIToAntigravityRequest.StoreGlobalThoughtSignature(decodedSig);
                    
                    if (signatureCache != null && state.ReasoningContent.Length > 0)
                    {
                        signatureCache.SetSignature(state.ReasoningContent.ToString(), decodedSig);
                    }
                }

                var textResult = JsonHelper.GetPath(part, "text");
                var functionCall = JsonHelper.GetPath(part, "functionCall");
                var inlineData = JsonHelper.GetPath(part, "inlineData") ?? JsonHelper.GetPath(part, "inline_data");
                var isThought = JsonHelper.GetBool(part, "thought") ?? false;

                // Skip signature-only parts
                if (!string.IsNullOrEmpty(thoughtSig) && textResult == null && functionCall == null && inlineData == null)
                {
                    continue;
                }

                if (textResult != null)
                {
                    var text = textResult.GetValue<string>();
                    
                    if (isThought)
                    {
                        delta["reasoning_content"] = text;
                        state.ReasoningContent.Append(text);
                    }
                    else
                    {
                        delta["content"] = text;
                    }
                    delta["role"] = "assistant";
                }
                else if (functionCall != null)
                {
                    hasFunctionCall = true;
                    
                    var toolCalls = delta["tool_calls"] as JsonArray;
                    if (toolCalls == null)
                    {
                        toolCalls = new JsonArray();
                        delta["tool_calls"] = toolCalls;
                    }

                    var fcName = JsonHelper.GetString(functionCall, "name") ?? "";
                    var uniqueId = Interlocked.Increment(ref _globalToolCallIdCounter);
                    state.ToolCallIdCounter++;
                    var fcId = $"call_{fcName}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{uniqueId}";

                    var fcObj = new JsonObject
                    {
                        ["id"] = fcId,
                        ["index"] = state.FunctionIndex,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = fcName,
                            ["arguments"] = ""
                        }
                    };

                    var fcArgs = JsonHelper.GetPath(functionCall, "args")?.DeepClone() as JsonObject;
                    if (fcArgs != null)
                    {
                        // Task 11.5.4: Remap function call args
                        RemapFunctionCallArgs(fcName, fcArgs);
                        (fcObj["function"] as JsonObject)!["arguments"] = JsonHelper.Stringify(fcArgs);
                    }

                    toolCalls.Add(fcObj);
                    delta["role"] = "assistant";
                    state.FunctionIndex++;
                }
                else if (inlineData != null)
                {
                    var data = JsonHelper.GetString(inlineData, "data");
                    if (string.IsNullOrEmpty(data)) continue;

                    var mimeType = JsonHelper.GetString(inlineData, "mimeType") 
                                ?? JsonHelper.GetString(inlineData, "mime_type") 
                                ?? "image/png";

                    var imageUrl = $"data:{mimeType};base64,{data}";

                    var images = delta["images"] as JsonArray;
                    if (images == null)
                    {
                        images = new JsonArray();
                        delta["images"] = images;
                    }

                    images.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["index"] = images.Count,
                        ["image_url"] = new JsonObject
                        {
                            ["url"] = imageUrl
                        }
                    });
                    delta["role"] = "assistant";
                }
            }
        }

        // Task 11.5.2: Handle grounding metadata
        var grounding = JsonHelper.GetPath(root, "response.candidates.0.groundingMetadata");
        if (grounding != null && delta != null)
        {
            var groundingText = FormatGroundingMetadata(grounding);
            if (!string.IsNullOrEmpty(groundingText))
            {
                var existingContent = JsonHelper.GetString(delta, "content") ?? "";
                delta["content"] = existingContent + groundingText;
            }
        }

        if (hasFunctionCall)
        {
            var choice = (template["choices"] as JsonArray)?[0] as JsonObject;
            choice!["finish_reason"] = "tool_calls";
            choice["native_finish_reason"] = "tool_calls";
        }

        results.Add(JsonHelper.Stringify(template));
        return results;
    }

    /// <summary>
    /// Convert non-streaming response.
    /// </summary>
    public static string ConvertNonStream(string modelName, byte[] rawJson, ISignatureCache? signatureCache = null)
    {
        var root = JsonHelper.Parse(rawJson);
        if (root == null) return "{}";

        // Get response node
        var responseNode = JsonHelper.GetPath(root, "response");
        if (responseNode == null)
        {
            if (JsonHelper.Exists(root, "candidates"))
                responseNode = root;
            else
                return "{}";
        }

        // Usage statistics
        var promptTokens = JsonHelper.GetLong(responseNode, "usageMetadata.promptTokenCount") ?? 0;
        var candidateTokens = JsonHelper.GetLong(responseNode, "usageMetadata.candidatesTokenCount") ?? 0;
        var thoughtTokens = JsonHelper.GetLong(responseNode, "usageMetadata.thoughtsTokenCount") ?? 0;
        var totalTokens = JsonHelper.GetLong(responseNode, "usageMetadata.totalTokenCount") ?? 0;
        var cachedTokens = JsonHelper.GetLong(responseNode, "usageMetadata.cachedContentTokenCount") ?? 0;

        var effectivePromptTokens = promptTokens - cachedTokens;
        if (effectivePromptTokens < 0) effectivePromptTokens = 0;

        var response = new JsonObject
        {
            ["id"] = JsonHelper.GetString(responseNode, "responseId") ?? $"chatcmpl-{Guid.NewGuid():N}",
            ["object"] = "chat.completion",
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["model"] = JsonHelper.GetString(responseNode, "modelVersion") ?? modelName,
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["index"] = 0,
                    ["message"] = new JsonObject
                    {
                        ["role"] = "assistant"
                    },
                    ["finish_reason"] = "stop"
                }
            },
            ["usage"] = new JsonObject
            {
                ["prompt_tokens"] = effectivePromptTokens,
                ["completion_tokens"] = candidateTokens + thoughtTokens,
                ["total_tokens"] = totalTokens
            }
        };

        if (cachedTokens > 0)
        {
            (response["usage"] as JsonObject)!["prompt_tokens_details"] = new JsonObject
            {
                ["cached_tokens"] = cachedTokens
            };
        }

        if (thoughtTokens > 0)
        {
            (response["usage"] as JsonObject)!["completion_tokens_details"] = new JsonObject
            {
                ["reasoning_tokens"] = thoughtTokens
            };
        }

        var message = ((response["choices"] as JsonArray)?[0] as JsonObject)?["message"] as JsonObject;
        var parts = JsonHelper.GetPath(responseNode, "candidates.0.content.parts") as JsonArray;
        
        var textBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        var toolCalls = new JsonArray();
        var images = new JsonArray();
        var hasToolCall = false;

        if (parts != null)
        {
            foreach (var part in parts)
            {
                // Task 11.5.1: Capture and store thought signature
                var thoughtSig = JsonHelper.GetString(part, "thoughtSignature") 
                              ?? JsonHelper.GetString(part, "thought_signature");
                if (!string.IsNullOrEmpty(thoughtSig))
                {
                    var decodedSig = DecodeBase64Signature(thoughtSig);
                    OpenAIToAntigravityRequest.StoreGlobalThoughtSignature(decodedSig);
                }

                var isThought = JsonHelper.GetBool(part, "thought") ?? false;
                var text = JsonHelper.GetString(part, "text");

                if (!string.IsNullOrEmpty(text))
                {
                    if (isThought)
                    {
                        reasoningBuilder.Append(text);
                        
                        // Store in signature cache if available
                        if (signatureCache != null && !string.IsNullOrEmpty(thoughtSig))
                        {
                            signatureCache.SetSignature(text, DecodeBase64Signature(thoughtSig));
                        }
                    }
                    else
                    {
                        textBuilder.Append(text);
                    }
                    continue;
                }

                var functionCall = JsonHelper.GetPath(part, "functionCall");
                if (functionCall != null)
                {
                    hasToolCall = true;

                    var fcName = JsonHelper.GetString(functionCall, "name") ?? "";
                    var fcArgs = JsonHelper.GetPath(functionCall, "args")?.DeepClone() as JsonObject;
                    
                    // Task 11.5.4: Remap function call args
                    if (fcArgs != null)
                    {
                        RemapFunctionCallArgs(fcName, fcArgs);
                    }
                    
                    var uniqueId = Interlocked.Increment(ref _globalToolCallIdCounter);
                    var fcId = $"call_{fcName}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{uniqueId}";

                    toolCalls.Add(new JsonObject
                    {
                        ["id"] = fcId,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = fcName,
                            ["arguments"] = fcArgs != null ? JsonHelper.Stringify(fcArgs) : "{}"
                        }
                    });
                    continue;
                }

                var inlineData = JsonHelper.GetPath(part, "inlineData") ?? JsonHelper.GetPath(part, "inline_data");
                if (inlineData != null)
                {
                    var data = JsonHelper.GetString(inlineData, "data");
                    if (!string.IsNullOrEmpty(data))
                    {
                        var mimeType = JsonHelper.GetString(inlineData, "mimeType") 
                                    ?? JsonHelper.GetString(inlineData, "mime_type") 
                                    ?? "image/png";
                        
                        var imageUrl = $"data:{mimeType};base64,{data}";
                        
                        images.Add(new JsonObject
                        {
                            ["type"] = "image_url",
                            ["index"] = images.Count,
                            ["image_url"] = new JsonObject
                            {
                                ["url"] = imageUrl
                            }
                        });
                    }
                }
            }
        }

        // Task 11.5.2: Handle grounding metadata
        var grounding = JsonHelper.GetPath(responseNode, "candidates.0.groundingMetadata");
        if (grounding != null)
        {
            var groundingText = FormatGroundingMetadata(grounding);
            if (!string.IsNullOrEmpty(groundingText))
            {
                textBuilder.Append(groundingText);
            }
        }

        // Set content
        if (textBuilder.Length > 0)
        {
            message!["content"] = textBuilder.ToString();
        }
        else
        {
            message!["content"] = (JsonNode?)null;
        }

        // Set reasoning_content
        if (reasoningBuilder.Length > 0)
        {
            message["reasoning_content"] = reasoningBuilder.ToString();
        }

        // Set tool_calls
        if (hasToolCall)
        {
            message["tool_calls"] = toolCalls;
            var choice = (response["choices"] as JsonArray)?[0] as JsonObject;
            choice!["finish_reason"] = "tool_calls";
        }
        else
        {
            var finishReason = JsonHelper.GetString(responseNode, "candidates.0.finishReason");
            var choice = (response["choices"] as JsonArray)?[0] as JsonObject;
            choice!["finish_reason"] = finishReason?.ToLower() switch
            {
                "max_tokens" => "length",
                "stop" => "stop",
                _ => "stop"
            };
        }

        // Set images if present
        if (images.Count > 0)
        {
            message["images"] = images;
        }

        return JsonHelper.Stringify(response);
    }
}
