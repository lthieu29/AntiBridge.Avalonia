using System.Text.Json.Nodes;
using AntiBridge.Core.Services;

namespace AntiBridge.Core.Translator;

/// <summary>
/// Converts Claude API requests to Antigravity API format
/// Ported from CLIProxyAPI/internal/translator/antigravity/claude/antigravity_claude_request.go
/// </summary>
public static class ClaudeToAntigravityRequest
{
    private const string SkipThoughtSignatureValidator = "skip_thought_signature_validator";
    
    /// <summary>
    /// Interleaved thinking hint text to inject when tools and thinking are both enabled.
    /// Requirement 4.2
    /// </summary>
    public const string InterleavedThinkingHintText = "Interleaved thinking is enabled. You may think between tool calls to reflect on tool outputs before proceeding.";

    /// <summary>
    /// Convert Claude request to Antigravity format
    /// </summary>
    /// <param name="modelName">The model name to use</param>
    /// <param name="rawJson">Raw JSON bytes of the Claude request</param>
    /// <param name="stream">Whether this is a streaming request</param>
    /// <param name="signatureCache">Optional signature cache for looking up cached signatures</param>
    public static byte[] Convert(string modelName, byte[] rawJson, bool stream, ISignatureCache? signatureCache = null)
    {
        var root = JsonHelper.Parse(rawJson);
        if (root == null) return rawJson;

        // Build output structure
        var output = new JsonObject
        {
            ["model"] = modelName,
            ["request"] = new JsonObject
            {
                ["contents"] = new JsonArray()
            }
        };

        // Check if we should inject thinking hint (before processing system instruction)
        var shouldInjectHint = ShouldInjectThinkingHint(root);

        // Process system instruction
        ProcessSystemInstruction(root, output);

        // Inject interleaved thinking hint if needed (Requirement 4.1, 4.3, 4.4)
        if (shouldInjectHint)
        {
            InjectThinkingHint(output);
        }

        // Process messages -> contents
        ProcessMessages(root, output, modelName, signatureCache);

        // Process tools
        ProcessTools(root, output);

        // Process generation config
        ProcessGenerationConfig(root, output);

        // Add safety settings
        AddSafetySettings(output);

        return System.Text.Encoding.UTF8.GetBytes(JsonHelper.Stringify(output));
    }

    /// <summary>
    /// Check if interleaved thinking hint should be injected.
    /// Returns true if request has both tools and thinking enabled.
    /// Requirements 4.1, 4.3
    /// </summary>
    /// <param name="root">The root JSON node of the request</param>
    /// <returns>True if hint should be injected</returns>
    public static bool ShouldInjectThinkingHint(JsonNode? root)
    {
        if (root == null) return false;

        // Check if tools are present
        var tools = JsonHelper.GetPath(root, "tools") as JsonArray;
        var hasTools = tools != null && tools.Count > 0;

        // Check if thinking is enabled
        var thinking = JsonHelper.GetPath(root, "thinking");
        var thinkingType = JsonHelper.GetString(thinking, "type");
        var hasThinking = thinkingType == "enabled";

        // Requirement 4.1, 4.3: Only inject if BOTH tools and thinking are enabled
        return hasTools && hasThinking;
    }

    /// <summary>
    /// Inject interleaved thinking hint at the end of system instruction parts.
    /// Requirements 4.2, 4.4
    /// </summary>
    /// <param name="output">The output JSON object</param>
    public static void InjectThinkingHint(JsonObject output)
    {
        var request = output["request"] as JsonObject;
        if (request == null) return;

        // Get or create system instruction
        var systemInstruction = request["systemInstruction"] as JsonObject;
        if (systemInstruction == null)
        {
            systemInstruction = new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray()
            };
            request["systemInstruction"] = systemInstruction;
        }

        // Get or create parts array
        var parts = systemInstruction["parts"] as JsonArray;
        if (parts == null)
        {
            parts = new JsonArray();
            systemInstruction["parts"] = parts;
        }

        // Requirement 4.4: Add hint at the end of system instruction parts
        parts.Add(new JsonObject { ["text"] = InterleavedThinkingHintText });
    }

    private static void ProcessSystemInstruction(JsonNode root, JsonObject output)
    {
        var systemNode = JsonHelper.GetPath(root, "system");
        if (systemNode == null) return;

        var systemInstruction = new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray()
        };

        if (systemNode is JsonArray systemArray)
        {
            foreach (var item in systemArray)
            {
                var type = JsonHelper.GetString(item, "type");
                if (type == "text")
                {
                    var text = JsonHelper.GetString(item, "text") ?? "";
                    var parts = systemInstruction["parts"] as JsonArray;
                    parts?.Add(new JsonObject { ["text"] = text });
                }
            }
        }
        else if (systemNode is JsonValue)
        {
            var text = systemNode.GetValue<string>();
            var parts = systemInstruction["parts"] as JsonArray;
            parts?.Add(new JsonObject { ["text"] = text });
        }

        var partsArray = systemInstruction["parts"] as JsonArray;
        if (partsArray?.Count > 0)
        {
            var request = output["request"] as JsonObject;
            request!["systemInstruction"] = systemInstruction;
        }
    }

    private static void ProcessMessages(JsonNode root, JsonObject output, string modelName, ISignatureCache? signatureCache)
    {
        var messages = JsonHelper.GetPath(root, "messages") as JsonArray;
        if (messages == null) return;

        var contents = (output["request"] as JsonObject)?["contents"] as JsonArray;
        if (contents == null) return;

        foreach (var message in messages)
        {
            var role = JsonHelper.GetString(message, "role");
            if (string.IsNullOrEmpty(role)) continue;

            var geminiRole = role == "assistant" ? "model" : role;
            var contentNode = new JsonObject
            {
                ["role"] = geminiRole,
                ["parts"] = new JsonArray()
            };

            var contentParts = contentNode["parts"] as JsonArray;
            var messageContent = JsonHelper.GetPath(message, "content");

            if (messageContent is JsonArray contentArray)
            {
                foreach (var content in contentArray)
                {
                    ProcessContentItem(content, contentParts!, modelName, signatureCache);
                }
            }
            else if (messageContent is JsonValue)
            {
                var text = messageContent.GetValue<string>();
                contentParts?.Add(new JsonObject { ["text"] = text });
            }

            // Requirement 3.1: Reorder parts for "model" role (thinking blocks first)
            if (geminiRole == "model" && contentParts?.Count > 0)
            {
                ReorderParts(contentParts);
            }

            if (contentParts?.Count > 0)
            {
                contents.Add(contentNode);
            }
        }
    }

    /// <summary>
    /// Reorder parts so that thinking blocks come first, preserving relative order within each group.
    /// Implements stable partitioning: thinking blocks first, then non-thinking parts.
    /// Requirements 3.1, 3.2, 3.3
    /// </summary>
    /// <param name="parts">The parts array to reorder in place</param>
    public static void ReorderParts(JsonArray parts)
    {
        if (parts == null || parts.Count <= 1)
            return;

        // Separate thinking and non-thinking parts while preserving order
        var thinkingParts = new List<JsonNode?>();
        var nonThinkingParts = new List<JsonNode?>();

        foreach (var part in parts)
        {
            if (IsThinkingPart(part))
            {
                thinkingParts.Add(part?.DeepClone());
            }
            else
            {
                nonThinkingParts.Add(part?.DeepClone());
            }
        }

        // Requirement 3.4: If no thinking blocks, keep original order
        if (thinkingParts.Count == 0)
            return;

        // Clear and rebuild the array with thinking parts first
        parts.Clear();

        // Add thinking parts first (preserving their relative order - Requirement 3.2)
        foreach (var part in thinkingParts)
        {
            parts.Add(part);
        }

        // Add non-thinking parts after (preserving their relative order - Requirement 3.3)
        foreach (var part in nonThinkingParts)
        {
            parts.Add(part);
        }
    }

    /// <summary>
    /// Check if a part is a thinking block (has thought=true property)
    /// </summary>
    private static bool IsThinkingPart(JsonNode? part)
    {
        if (part == null) return false;
        
        var thought = JsonHelper.GetBool(part, "thought");
        return thought == true;
    }

    private static void ProcessContentItem(JsonNode? content, JsonArray parts, string modelName, ISignatureCache? signatureCache)
    {
        if (content == null) return;

        var type = JsonHelper.GetString(content, "type");

        switch (type)
        {
            case "text":
                var text = JsonHelper.GetString(content, "text") ?? "";
                parts.Add(new JsonObject { ["text"] = text });
                break;

            case "thinking":
                var thinkingText = JsonHelper.GetString(content, "thinking") ?? "";
                var clientSignature = JsonHelper.GetString(content, "signature") ?? "";
                
                // Requirement 1.2: Lookup signature from cache first
                // Requirement 1.3: If cache hit, use cached signature
                // Requirement 1.4: If cache miss, fallback to client signature
                var signatureToUse = clientSignature;
                
                if (signatureCache != null && !string.IsNullOrEmpty(thinkingText))
                {
                    var cachedSignature = signatureCache.GetSignature(thinkingText);
                    if (cachedSignature != null)
                    {
                        // Cache hit - use cached signature
                        signatureToUse = cachedSignature;
                    }
                    // Cache miss - keep using client signature (already set)
                }
                
                var thinkingPart = new JsonObject
                {
                    ["thought"] = true,
                    ["text"] = thinkingText
                };
                
                if (!string.IsNullOrEmpty(signatureToUse))
                {
                    // Extract signature after model prefix
                    var sigParts = signatureToUse.Split('#', 2);
                    if (sigParts.Length == 2)
                        thinkingPart["thoughtSignature"] = sigParts[1];
                    else
                        thinkingPart["thoughtSignature"] = signatureToUse;
                }
                
                parts.Add(thinkingPart);
                break;

            case "tool_use":
                var funcName = JsonHelper.GetString(content, "name") ?? "";
                var funcId = JsonHelper.GetString(content, "id") ?? "";
                var inputNode = JsonHelper.GetPath(content, "input");
                
                var toolPart = new JsonObject
                {
                    ["thoughtSignature"] = SkipThoughtSignatureValidator,
                    ["functionCall"] = new JsonObject
                    {
                        ["id"] = funcId,
                        ["name"] = funcName,
                        ["args"] = inputNode?.DeepClone() ?? new JsonObject()
                    }
                };
                parts.Add(toolPart);
                break;

            case "tool_result":
                var toolCallId = JsonHelper.GetString(content, "tool_use_id") ?? "";
                var resultContent = JsonHelper.GetPath(content, "content");
                
                // Extract function name from tool_call_id
                var idParts = toolCallId.Split('-');
                var funcNameFromId = idParts.Length > 2 
                    ? string.Join("-", idParts.Take(idParts.Length - 2))
                    : toolCallId;

                var responsePart = new JsonObject
                {
                    ["functionResponse"] = new JsonObject
                    {
                        ["id"] = toolCallId,
                        ["name"] = funcNameFromId,
                        ["response"] = new JsonObject
                        {
                            ["result"] = resultContent?.DeepClone() ?? JsonValue.Create("")
                        }
                    }
                };
                parts.Add(responsePart);
                break;

            case "image":
                var source = JsonHelper.GetPath(content, "source");
                var sourceType = JsonHelper.GetString(source, "type");
                
                if (sourceType == "base64")
                {
                    var mimeType = JsonHelper.GetString(source, "media_type") ?? "image/png";
                    var data = JsonHelper.GetString(source, "data") ?? "";
                    
                    parts.Add(new JsonObject
                    {
                        ["inlineData"] = new JsonObject
                        {
                            ["mime_type"] = mimeType,
                            ["data"] = data
                        }
                    });
                }
                break;
        }
    }

    private static void ProcessTools(JsonNode root, JsonObject output)
    {
        var tools = JsonHelper.GetPath(root, "tools") as JsonArray;
        if (tools == null || tools.Count == 0) return;

        var functionDeclarations = new JsonArray();

        foreach (var tool in tools)
        {
            var inputSchema = JsonHelper.GetPath(tool, "input_schema");
            if (inputSchema == null) continue;

            var funcDecl = new JsonObject
            {
                ["name"] = JsonHelper.GetString(tool, "name"),
                ["description"] = JsonHelper.GetString(tool, "description")
            };

            // Clean schema for Antigravity compatibility
            var cleanedSchema = CleanJsonSchema(inputSchema);
            funcDecl["parametersJsonSchema"] = cleanedSchema;

            functionDeclarations.Add(funcDecl);
        }

        if (functionDeclarations.Count > 0)
        {
            var request = output["request"] as JsonObject;
            request!["tools"] = new JsonArray
            {
                new JsonObject { ["functionDeclarations"] = functionDeclarations }
            };
        }
    }

    private static JsonNode? CleanJsonSchema(JsonNode? schema)
    {
        if (schema == null) return null;
        
        var clone = schema.DeepClone();
        if (clone is JsonObject obj)
        {
            // Remove unsupported keywords
            obj.Remove("$schema");
            obj.Remove("additionalProperties");
            obj.Remove("default");
            
            // Recursively clean properties
            if (obj["properties"] is JsonObject props)
            {
                foreach (var prop in props)
                {
                    if (prop.Value is JsonObject propObj)
                    {
                        propObj.Remove("default");
                        propObj.Remove("additionalProperties");
                    }
                }
            }
        }
        
        return clone;
    }

    private static void ProcessGenerationConfig(JsonNode root, JsonObject output)
    {
        var request = output["request"] as JsonObject;
        var genConfig = new JsonObject();
        var hasConfig = false;

        // Temperature
        var temp = JsonHelper.GetDouble(root, "temperature");
        if (temp.HasValue)
        {
            genConfig["temperature"] = temp.Value;
            hasConfig = true;
        }

        // Top P
        var topP = JsonHelper.GetDouble(root, "top_p");
        if (topP.HasValue)
        {
            genConfig["topP"] = topP.Value;
            hasConfig = true;
        }

        // Top K
        var topK = JsonHelper.GetInt(root, "top_k");
        if (topK.HasValue)
        {
            genConfig["topK"] = topK.Value;
            hasConfig = true;
        }

        // Max tokens
        var maxTokens = JsonHelper.GetInt(root, "max_tokens");
        if (maxTokens.HasValue)
        {
            genConfig["maxOutputTokens"] = maxTokens.Value;
            hasConfig = true;
        }

        // Thinking config
        var thinking = JsonHelper.GetPath(root, "thinking");
        if (thinking != null)
        {
            var thinkingType = JsonHelper.GetString(thinking, "type");
            if (thinkingType == "enabled")
            {
                var budget = JsonHelper.GetInt(thinking, "budget_tokens");
                if (budget.HasValue)
                {
                    genConfig["thinkingConfig"] = new JsonObject
                    {
                        ["thinkingBudget"] = budget.Value,
                        ["includeThoughts"] = true
                    };
                    hasConfig = true;
                }
            }
        }

        if (hasConfig)
        {
            request!["generationConfig"] = genConfig;
        }
    }

    private static void AddSafetySettings(JsonObject output)
    {
        var request = output["request"] as JsonObject;
        request!["safetySettings"] = new JsonArray
        {
            new JsonObject { ["category"] = "HARM_CATEGORY_HATE_SPEECH", ["threshold"] = "OFF" },
            new JsonObject { ["category"] = "HARM_CATEGORY_DANGEROUS_CONTENT", ["threshold"] = "OFF" },
            new JsonObject { ["category"] = "HARM_CATEGORY_SEXUALLY_EXPLICIT", ["threshold"] = "OFF" },
            new JsonObject { ["category"] = "HARM_CATEGORY_HARASSMENT", ["threshold"] = "OFF" }
        };
    }
}
