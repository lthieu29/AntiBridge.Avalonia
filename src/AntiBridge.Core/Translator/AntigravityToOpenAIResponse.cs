using System.Text.Json.Nodes;

namespace AntiBridge.Core.Translator;

/// <summary>
/// State for OpenAI streaming response conversion
/// </summary>
public class OpenAIStreamState
{
    public long UnixTimestamp { get; set; }
    public int FunctionIndex { get; set; }
}

/// <summary>
/// Converts Antigravity API responses to OpenAI API format
/// Ported from CLIProxyAPI/internal/translator/antigravity/openai/chat-completions/antigravity_openai_response.go
/// </summary>
public static class AntigravityToOpenAIResponse
{
    private static long _functionCallIdCounter;

    /// <summary>
    /// Convert streaming response chunk
    /// </summary>
    public static List<string> ConvertStream(string modelName, byte[] rawJson, OpenAIStreamState state)
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
                    ["delta"] = new JsonObject
                    {
                        ["role"] = (JsonNode?)null,
                        ["content"] = (JsonNode?)null,
                        ["reasoning_content"] = (JsonNode?)null,
                        ["tool_calls"] = (JsonNode?)null
                    },
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
            var promptTokens = (JsonHelper.GetLong(usage, "promptTokenCount") ?? 0) - cachedTokens;
            var thoughtsTokens = JsonHelper.GetLong(usage, "thoughtsTokenCount") ?? 0;
            var candidatesTokens = JsonHelper.GetLong(usage, "candidatesTokenCount") ?? 0;
            var totalTokens = JsonHelper.GetLong(usage, "totalTokenCount") ?? 0;

            var usageObj = new JsonObject
            {
                ["prompt_tokens"] = promptTokens + thoughtsTokens,
                ["completion_tokens"] = candidatesTokens,
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

        if (parts != null)
        {
            var delta = ((template["choices"] as JsonArray)?[0] as JsonObject)?["delta"] as JsonObject;

            foreach (var part in parts)
            {
                var textResult = JsonHelper.GetPath(part, "text");
                var functionCall = JsonHelper.GetPath(part, "functionCall");
                var inlineData = JsonHelper.GetPath(part, "inlineData") ?? JsonHelper.GetPath(part, "inline_data");
                var thoughtSig = JsonHelper.GetString(part, "thoughtSignature") 
                              ?? JsonHelper.GetString(part, "thought_signature");
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
                        delta!["reasoning_content"] = text;
                    }
                    else
                    {
                        delta!["content"] = text;
                    }
                    delta!["role"] = "assistant";
                }
                else if (functionCall != null)
                {
                    hasFunctionCall = true;
                    
                    var toolCalls = delta!["tool_calls"] as JsonArray;
                    if (toolCalls == null)
                    {
                        toolCalls = new JsonArray();
                        delta["tool_calls"] = toolCalls;
                    }

                    var fcName = JsonHelper.GetString(functionCall, "name") ?? "";
                    var fcId = $"{fcName}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Interlocked.Increment(ref _functionCallIdCounter)}";

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

                    var fcArgs = JsonHelper.GetPath(functionCall, "args");
                    if (fcArgs != null)
                    {
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

                    var images = delta!["images"] as JsonArray;
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
    /// Convert non-streaming response
    /// </summary>
    public static string ConvertNonStream(string modelName, byte[] rawJson)
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

        var promptTokens = JsonHelper.GetLong(responseNode, "usageMetadata.promptTokenCount") ?? 0;
        var candidateTokens = JsonHelper.GetLong(responseNode, "usageMetadata.candidatesTokenCount") ?? 0;
        var thoughtTokens = JsonHelper.GetLong(responseNode, "usageMetadata.thoughtsTokenCount") ?? 0;
        var totalTokens = JsonHelper.GetLong(responseNode, "usageMetadata.totalTokenCount") ?? 0;
        var cachedTokens = JsonHelper.GetLong(responseNode, "usageMetadata.cachedContentTokenCount") ?? 0;

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
                        ["role"] = "assistant",
                        ["content"] = (JsonNode?)null
                    },
                    ["finish_reason"] = "stop"
                }
            },
            ["usage"] = new JsonObject
            {
                ["prompt_tokens"] = promptTokens - cachedTokens,
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
        
        var textBuilder = new System.Text.StringBuilder();
        var reasoningBuilder = new System.Text.StringBuilder();
        var toolCalls = new JsonArray();
        var hasToolCall = false;
        var toolIdCounter = 0;

        if (parts != null)
        {
            foreach (var part in parts)
            {
                var isThought = JsonHelper.GetBool(part, "thought") ?? false;
                var text = JsonHelper.GetString(part, "text");

                if (!string.IsNullOrEmpty(text))
                {
                    if (isThought)
                        reasoningBuilder.Append(text);
                    else
                        textBuilder.Append(text);
                    continue;
                }

                var functionCall = JsonHelper.GetPath(part, "functionCall");
                if (functionCall != null)
                {
                    hasToolCall = true;
                    toolIdCounter++;

                    var fcName = JsonHelper.GetString(functionCall, "name") ?? "";
                    var fcArgs = JsonHelper.GetPath(functionCall, "args");

                    toolCalls.Add(new JsonObject
                    {
                        ["id"] = $"call_{toolIdCounter}",
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = fcName,
                            ["arguments"] = fcArgs != null ? JsonHelper.Stringify(fcArgs) : "{}"
                        }
                    });
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
                        // For non-streaming, we could add image to content array
                        // but OpenAI format typically uses text content
                    }
                }
            }
        }

        if (textBuilder.Length > 0)
        {
            message!["content"] = textBuilder.ToString();
        }

        if (reasoningBuilder.Length > 0)
        {
            message!["reasoning_content"] = reasoningBuilder.ToString();
        }

        if (hasToolCall)
        {
            message!["tool_calls"] = toolCalls;
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

        return JsonHelper.Stringify(response);
    }
}
