using System.Text.Json.Nodes;
using AntiBridge.Core.Services;

namespace AntiBridge.Core.Translator;

/// <summary>
/// Converts OpenAI API requests to Antigravity API format
/// Ported from Antigravity-Manager/src-tauri/src/proxy/mappers/openai/request.rs
/// </summary>
public static class OpenAIToAntigravityRequest
{
    private const string SkipThoughtSignatureValidator = "skip_thought_signature_validator";

    /// <summary>
    /// Mapping from reasoning_effort values to thinking budget tokens.
    /// </summary>
    private static readonly Dictionary<string, int> ReasoningEffortBudgets = new()
    {
        ["none"] = 0,
        ["low"] = 1024,
        ["medium"] = 8192,
        ["high"] = 32768,
        ["auto"] = -1  // -1 indicates auto/unlimited
    };

    /// <summary>
    /// Global thought signature storage for reuse across requests.
    /// Task 11.2.5: Implement global thought signature storage/retrieval.
    /// </summary>
    private static string? _globalThoughtSignature;
    private static readonly object _signatureLock = new();

    #region Task 11.2: Thinking Model Support

    /// <summary>
    /// Checks if a model is a thinking model (Gemini 3 or Claude thinking variants).
    /// Task 11.2.1: Implement IsThinkingModel() detection.
    /// Ported from Rust: is_gemini_3_thinking, is_claude_thinking
    /// </summary>
    public static bool IsThinkingModel(string modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return false;
        
        var lower = modelName.ToLowerInvariant();
        
        // Gemini 3 thinking models
        var isGemini3Thinking = lower.Contains("gemini-3") &&
            (lower.EndsWith("-high") || lower.EndsWith("-low") || lower.Contains("-pro"));
        
        // Claude thinking models
        var isClaudeThinking = lower.EndsWith("-thinking");
        
        return isGemini3Thinking || isClaudeThinking;
    }

    /// <summary>
    /// Checks if message history has incompatible assistant messages for thinking models.
    /// An assistant message is incompatible if it has no reasoning_content.
    /// Task 11.2.2: Implement HasIncompatibleAssistantHistory().
    /// Ported from Rust: has_incompatible_assistant_history
    /// </summary>
    public static bool HasIncompatibleAssistantHistory(JsonArray? messages)
    {
        if (messages == null) return false;

        foreach (var msg in messages)
        {
            var role = JsonHelper.GetString(msg, "role");
            if (role != "assistant") continue;

            var reasoningContent = JsonHelper.GetString(msg, "reasoning_content");
            if (string.IsNullOrEmpty(reasoningContent))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the global thought signature for reuse.
    /// Task 11.2.5
    /// </summary>
    public static string? GetGlobalThoughtSignature()
    {
        lock (_signatureLock)
        {
            return _globalThoughtSignature;
        }
    }

    /// <summary>
    /// Stores a thought signature globally for reuse.
    /// Task 11.2.5
    /// </summary>
    public static void StoreGlobalThoughtSignature(string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return;
        lock (_signatureLock)
        {
            _globalThoughtSignature = signature;
        }
    }

    #endregion

    #region Task 11.3: Tool Processing Fixes

    /// <summary>
    /// Enforces uppercase type values for Gemini compatibility.
    /// Task 11.3.4: Implement EnforceUppercaseTypes().
    /// Ported from Rust: enforce_uppercase_types()
    /// </summary>
    public static void EnforceUppercaseTypes(JsonNode? node)
    {
        if (node == null) return;

        if (node is JsonObject obj)
        {
            // Convert type to uppercase
            if (obj.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonValue typeVal)
            {
                try
                {
                    var typeStr = typeVal.GetValue<string>();
                    obj["type"] = typeStr.ToUpperInvariant();
                }
                catch { }
            }

            // Recurse into properties
            if (obj.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject props)
            {
                foreach (var kvp in props.ToList())
                {
                    EnforceUppercaseTypes(kvp.Value);
                }
            }

            // Recurse into items
            if (obj.TryGetPropertyValue("items", out var itemsNode))
            {
                EnforceUppercaseTypes(itemsNode);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                EnforceUppercaseTypes(item);
            }
        }
    }

    /// <summary>
    /// Cleans JSON schema by removing invalid fields for Gemini.
    /// Task 11.3.3: Implement CleanJsonSchema().
    /// Ported from Rust: clean_json_schema()
    /// </summary>
    public static void CleanJsonSchema(JsonNode? node)
    {
        if (node == null) return;

        if (node is JsonObject obj)
        {
            // Remove invalid fields at this level
            obj.Remove("format");
            obj.Remove("strict");
            obj.Remove("additionalProperties");
            obj.Remove("definitions");
            obj.Remove("$ref");
            obj.Remove("$schema");

            // Recurse into all child objects
            foreach (var kvp in obj.ToList())
            {
                if (kvp.Value is JsonObject || kvp.Value is JsonArray)
                {
                    CleanJsonSchema(kvp.Value);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                CleanJsonSchema(item);
            }
        }
    }

    /// <summary>
    /// Fixes tool call arguments using the original schema.
    /// Task 11.3.2: Implement FixToolCallArgs().
    /// Ported from Rust: fix_tool_call_args()
    /// </summary>
    public static void FixToolCallArgs(JsonObject? args, JsonNode? schema)
    {
        if (args == null || schema == null) return;

        var properties = JsonHelper.GetPath(schema, "properties") as JsonObject;
        if (properties == null) return;

        foreach (var kvp in args.ToList())
        {
            var propSchema = properties[kvp.Key];
            if (propSchema == null) continue;

            var expectedType = JsonHelper.GetString(propSchema, "type")?.ToLowerInvariant();
            if (string.IsNullOrEmpty(expectedType)) continue;

            var currentValue = kvp.Value;

            // Fix type mismatches
            if (expectedType == "string" && currentValue is JsonValue val)
            {
                // Ensure it's a string
                try
                {
                    var numVal = val.GetValue<double>();
                    args[kvp.Key] = numVal.ToString();
                }
                catch { }
            }
            else if ((expectedType == "number" || expectedType == "integer") && currentValue is JsonValue strVal)
            {
                // Try to convert string to number
                try
                {
                    var str = strVal.GetValue<string>();
                    if (double.TryParse(str, out var num))
                    {
                        args[kvp.Key] = expectedType == "integer" ? (int)num : num;
                    }
                }
                catch { }
            }
            else if (expectedType == "boolean" && currentValue is JsonValue boolVal)
            {
                // Try to convert string to boolean
                try
                {
                    var str = boolVal.GetValue<string>();
                    if (bool.TryParse(str, out var b))
                    {
                        args[kvp.Key] = b;
                    }
                }
                catch { }
            }
        }
    }

    #endregion

    #region Task 11.1: Request Preprocessing

    /// <summary>
    /// Merges consecutive messages with the same role.
    /// Gemini requires strict user/model alternation.
    /// Task 11.1.2: Implement MergeConsecutiveMessages().
    /// Ported from Rust: merged_contents logic
    /// </summary>
    public static bool MergeConsecutiveContents(JsonArray? contents)
    {
        if (contents == null || contents.Count <= 1) return false;

        var merged = new JsonArray();
        JsonObject? currentMsg = null;
        string? currentRole = null;
        bool anyMerged = false;

        foreach (var msg in contents)
        {
            if (msg == null) continue;

            var role = JsonHelper.GetString(msg, "role");
            if (string.IsNullOrEmpty(role)) continue;

            if (currentMsg == null || currentRole != role)
            {
                if (currentMsg != null)
                {
                    merged.Add(currentMsg);
                }
                currentMsg = msg.DeepClone() as JsonObject;
                currentRole = role;
            }
            else
            {
                // Same role - merge parts
                anyMerged = true;
                var currentParts = currentMsg["parts"] as JsonArray;
                var msgParts = msg["parts"] as JsonArray;
                
                if (currentParts != null && msgParts != null)
                {
                    foreach (var part in msgParts)
                    {
                        currentParts.Add(part?.DeepClone());
                    }
                }
            }
        }

        if (currentMsg != null)
        {
            merged.Add(currentMsg);
        }

        if (anyMerged)
        {
            contents.Clear();
            foreach (var msg in merged)
            {
                contents.Add(msg?.DeepClone());
            }
        }

        return anyMerged;
    }

    #endregion

    /// <summary>
    /// Convert OpenAI Chat Completions request to Antigravity format
    /// </summary>
    public static byte[] Convert(string modelName, byte[] rawJson, bool stream, ISignatureCache? signatureCache = null)
    {
        var root = JsonHelper.Parse(rawJson);
        if (root == null) return rawJson;

        // Task 11.1.1: Deep clean [undefined] strings
        JsonHelper.DeepCleanUndefined(root);

        // Task 11.1.3: Clean cache_control fields
        var messages = JsonHelper.GetPath(root, "messages") as JsonArray;
        if (messages != null)
        {
            JsonHelper.DeepCleanCacheControl(root);
        }

        // Task 11.2.1: Detect thinking model
        var isThinkingModel = IsThinkingModel(modelName);
        
        // Task 11.2.2: Check for incompatible history
        var hasIncompatibleHistory = HasIncompatibleAssistantHistory(messages);
        
        // Task 11.2.5: Get global thought signature
        var globalThoughtSig = GetGlobalThoughtSignature();
        
        // Determine if thinking should be enabled
        var actualIncludeThinking = isThinkingModel;
        if (isThinkingModel && hasIncompatibleHistory && string.IsNullOrEmpty(globalThoughtSig))
        {
            // Disable thinking to avoid 400 error
            actualIncludeThinking = false;
        }

        // Build output structure
        var output = new JsonObject
        {
            ["project"] = "",
            ["model"] = modelName,
            ["request"] = new JsonObject
            {
                ["contents"] = new JsonArray()
            }
        };

        // Task 11.4.1: Handle instructions field (priority over system)
        var instructions = JsonHelper.GetString(root, "instructions");

        // Process generation config (including reasoning_effort and thinking)
        ProcessGenerationConfig(root, output, actualIncludeThinking);

        // Process system instruction (with instructions field support)
        ProcessSystemInstruction(root, output, instructions);

        // Process messages (with tool call ID tracking and thinking support)
        ProcessMessages(root, output, modelName, actualIncludeThinking, globalThoughtSig, signatureCache);

        // Task 11.1.2: Merge consecutive contents
        var contents = (output["request"] as JsonObject)?["contents"] as JsonArray;
        MergeConsecutiveContents(contents);

        // Process tools (with schema cleaning)
        ProcessTools(root, output);

        // Add safety settings
        AddSafetySettings(output);

        // Final deep clean
        JsonHelper.DeepCleanUndefined(output);

        return System.Text.Encoding.UTF8.GetBytes(JsonHelper.Stringify(output));
    }

    /// <summary>
    /// Process generation config including reasoning_effort and thinking config.
    /// </summary>
    private static void ProcessGenerationConfig(JsonNode root, JsonObject output, bool includeThinking)
    {
        var request = output["request"] as JsonObject;
        var genConfig = new JsonObject();
        var hasConfig = false;

        // Reasoning effort -> thinking config
        var reasoningEffort = JsonHelper.GetString(root, "reasoning_effort")?.ToLower().Trim();
        if (!string.IsNullOrEmpty(reasoningEffort))
        {
            var thinkingConfig = new JsonObject();
            
            if (ReasoningEffortBudgets.TryGetValue(reasoningEffort, out var budget))
            {
                if (budget == 0)
                {
                    thinkingConfig["includeThoughts"] = false;
                }
                else if (budget == -1)
                {
                    thinkingConfig["thinkingBudget"] = -1;
                    thinkingConfig["includeThoughts"] = true;
                }
                else
                {
                    thinkingConfig["thinkingBudget"] = budget;
                    thinkingConfig["includeThoughts"] = true;
                }
            }
            else
            {
                thinkingConfig["thinkingLevel"] = reasoningEffort;
                thinkingConfig["includeThoughts"] = reasoningEffort != "none";
            }
            
            genConfig["thinkingConfig"] = thinkingConfig;
            hasConfig = true;
        }
        else if (includeThinking)
        {
            // Task 11.2.3: Inject thinking config for thinking models
            genConfig["thinkingConfig"] = new JsonObject
            {
                ["includeThoughts"] = true,
                ["thinkingBudget"] = 16000
            };
            hasConfig = true;
        }

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
        else
        {
            genConfig["maxOutputTokens"] = 16384;
            hasConfig = true;
        }

        // Candidate count (n parameter)
        var n = JsonHelper.GetInt(root, "n");
        if (n.HasValue && n.Value > 1)
        {
            genConfig["candidateCount"] = n.Value;
            hasConfig = true;
        }

        // Modalities
        var modalities = JsonHelper.GetPath(root, "modalities") as JsonArray;
        if (modalities != null)
        {
            var responseMods = new JsonArray();
            foreach (var mod in modalities)
            {
                var modStr = mod?.GetValue<string>()?.ToLower();
                if (modStr == "text") responseMods.Add("TEXT");
                else if (modStr == "image") responseMods.Add("IMAGE");
            }
            if (responseMods.Count > 0)
            {
                genConfig["responseModalities"] = responseMods;
                hasConfig = true;
            }
        }

        // Stop sequences
        var stop = JsonHelper.GetPath(root, "stop");
        if (stop != null)
        {
            if (stop is JsonValue stopVal)
            {
                try
                {
                    genConfig["stopSequences"] = new JsonArray { stopVal.GetValue<string>() };
                    hasConfig = true;
                }
                catch { }
            }
            else if (stop is JsonArray stopArr)
            {
                genConfig["stopSequences"] = stopArr.DeepClone();
                hasConfig = true;
            }
        }

        // Response format
        var responseFormat = JsonHelper.GetPath(root, "response_format");
        if (responseFormat != null)
        {
            var formatType = JsonHelper.GetString(responseFormat, "type");
            if (formatType == "json_object")
            {
                genConfig["responseMimeType"] = "application/json";
                hasConfig = true;
            }
        }

        if (hasConfig)
        {
            request!["generationConfig"] = genConfig;
        }
    }

    /// <summary>
    /// Process system instruction with instructions field support.
    /// Task 11.4.1: Handle instructions field (priority over system messages).
    /// </summary>
    private static void ProcessSystemInstruction(JsonNode root, JsonObject output, string? instructions)
    {
        var request = output["request"] as JsonObject;
        var messages = JsonHelper.GetPath(root, "messages") as JsonArray;
        
        var systemParts = new JsonArray();

        // Task 11.4.1: Instructions field has priority
        if (!string.IsNullOrEmpty(instructions))
        {
            systemParts.Add(new JsonObject { ["text"] = instructions });
        }

        // Extract system/developer messages
        if (messages != null)
        {
            foreach (var msg in messages)
            {
                var role = JsonHelper.GetString(msg, "role");
                if (role != "system" && role != "developer") continue;

                var content = JsonHelper.GetPath(msg, "content");
                if (content is JsonValue contentVal)
                {
                    try
                    {
                        systemParts.Add(new JsonObject { ["text"] = contentVal.GetValue<string>() });
                    }
                    catch { }
                }
                else if (content is JsonArray contentArr)
                {
                    foreach (var item in contentArr)
                    {
                        var text = JsonHelper.GetString(item, "text");
                        if (!string.IsNullOrEmpty(text))
                        {
                            systemParts.Add(new JsonObject { ["text"] = text });
                        }
                    }
                }
            }
        }

        if (systemParts.Count > 0)
        {
            request!["systemInstruction"] = new JsonObject
            {
                ["role"] = "user",
                ["parts"] = systemParts
            };
        }
    }

    /// <summary>
    /// Process messages with thinking support and tool call tracking.
    /// </summary>
    private static void ProcessMessages(JsonNode root, JsonObject output, string modelName, 
        bool includeThinking, string? globalThoughtSig, ISignatureCache? signatureCache)
    {
        var messages = JsonHelper.GetPath(root, "messages") as JsonArray;
        if (messages == null) return;

        var request = output["request"] as JsonObject;
        var contents = request!["contents"] as JsonArray;

        // Build tool call ID to name map
        var tcIdToName = new Dictionary<string, string>();
        // Task 11.3.1: Build tool name to schema map
        var toolNameToSchema = new Dictionary<string, JsonNode>();
        
        // First pass: collect tool mappings
        foreach (var msg in messages)
        {
            var role = JsonHelper.GetString(msg, "role");
            if (role == "assistant")
            {
                var toolCalls = JsonHelper.GetPath(msg, "tool_calls") as JsonArray;
                if (toolCalls != null)
                {
                    foreach (var tc in toolCalls)
                    {
                        if (JsonHelper.GetString(tc, "type") == "function")
                        {
                            var id = JsonHelper.GetString(tc, "id") ?? "";
                            var name = JsonHelper.GetString(tc, "function.name") ?? "";
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            {
                                // Task 11.3.6: Handle local_shell_call → shell remapping
                                var finalName = name == "local_shell_call" ? "shell" : name;
                                tcIdToName[id] = finalName;
                            }
                        }
                    }
                }
            }
        }

        // Collect tool schemas from request
        var tools = JsonHelper.GetPath(root, "tools") as JsonArray;
        if (tools != null)
        {
            foreach (var tool in tools)
            {
                var name = JsonHelper.GetString(tool, "function.name");
                var parameters = JsonHelper.GetPath(tool, "function.parameters");
                if (!string.IsNullOrEmpty(name) && parameters != null)
                {
                    toolNameToSchema[name] = parameters;
                }
            }
        }

        // Build tool responses cache
        var toolResponses = new Dictionary<string, JsonNode?>();
        foreach (var msg in messages)
        {
            var role = JsonHelper.GetString(msg, "role");
            if (role == "tool")
            {
                var toolCallId = JsonHelper.GetString(msg, "tool_call_id") ?? "";
                if (!string.IsNullOrEmpty(toolCallId))
                {
                    toolResponses[toolCallId] = JsonHelper.GetPath(msg, "content");
                }
            }
        }

        // Process each message
        foreach (var msg in messages)
        {
            var role = JsonHelper.GetString(msg, "role");
            var content = JsonHelper.GetPath(msg, "content");

            // Skip system/developer messages (handled separately)
            if (role == "system" || role == "developer") continue;
            // Skip tool messages (handled with assistant messages)
            if (role == "tool" || role == "function") continue;

            if (role == "user")
            {
                ProcessUserMessage(msg, contents);
            }
            else if (role == "assistant")
            {
                ProcessAssistantMessage(msg, contents, includeThinking, globalThoughtSig, 
                    modelName, tcIdToName, toolResponses, toolNameToSchema, signatureCache);
            }
        }
    }

    /// <summary>
    /// Process user message with image handling.
    /// Task 11.4.2: Implement local file path handling.
    /// </summary>
    private static void ProcessUserMessage(JsonNode msg, JsonArray? contents)
    {
        var content = JsonHelper.GetPath(msg, "content");
        var node = new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray()
        };
        var parts = node["parts"] as JsonArray;

        if (content is JsonValue contentVal)
        {
            try
            {
                parts?.Add(new JsonObject { ["text"] = contentVal.GetValue<string>() });
            }
            catch { }
        }
        else if (content is JsonArray contentArr)
        {
            foreach (var item in contentArr)
            {
                var type = JsonHelper.GetString(item, "type");
                switch (type)
                {
                    case "text":
                        var text = JsonHelper.GetString(item, "text") ?? "";
                        if (!string.IsNullOrEmpty(text))
                            parts?.Add(new JsonObject { ["text"] = text });
                        break;

                    case "image_url":
                        ProcessImageUrl(item, parts);
                        break;
                }
            }
        }

        if (parts?.Count > 0)
            contents?.Add(node);
    }

    /// <summary>
    /// Process image URL with local file support.
    /// Task 11.4.2: Implement local file path handling (file:// URLs → base64 inline data).
    /// </summary>
    private static void ProcessImageUrl(JsonNode item, JsonArray? parts)
    {
        var imageUrl = JsonHelper.GetString(item, "image_url.url") ?? "";
        
        if (imageUrl.StartsWith("data:") && imageUrl.Length > 5)
        {
            // Base64 data URL
            var dataStart = imageUrl.IndexOf(';');
            var base64Start = imageUrl.IndexOf(",base64,");
            if (dataStart > 5 && base64Start > dataStart)
            {
                var mime = imageUrl[5..dataStart];
                var data = imageUrl[(base64Start + 8)..];
                parts?.Add(new JsonObject
                {
                    ["inlineData"] = new JsonObject
                    {
                        ["mimeType"] = mime,
                        ["data"] = data
                    },
                    ["thoughtSignature"] = SkipThoughtSignatureValidator
                });
            }
        }
        else if (imageUrl.StartsWith("http://") || imageUrl.StartsWith("https://"))
        {
            // HTTP URL - pass as fileData
            parts?.Add(new JsonObject
            {
                ["fileData"] = new JsonObject
                {
                    ["fileUri"] = imageUrl,
                    ["mimeType"] = "image/jpeg"
                }
            });
        }
        else if (imageUrl.StartsWith("file://") || !imageUrl.Contains("://"))
        {
            // Task 11.4.2: Local file path handling
            var filePath = imageUrl;
            if (imageUrl.StartsWith("file:///"))
            {
                filePath = imageUrl[8..]; // Remove file:///
            }
            else if (imageUrl.StartsWith("file://"))
            {
                filePath = imageUrl[7..]; // Remove file://
            }

            try
            {
                if (File.Exists(filePath))
                {
                    var fileBytes = File.ReadAllBytes(filePath);
                    var base64Data = System.Convert.ToBase64String(fileBytes);
                    
                    // Determine MIME type from extension
                    var mimeType = filePath.ToLowerInvariant() switch
                    {
                        var p when p.EndsWith(".png") => "image/png",
                        var p when p.EndsWith(".gif") => "image/gif",
                        var p when p.EndsWith(".webp") => "image/webp",
                        _ => "image/jpeg"
                    };

                    parts?.Add(new JsonObject
                    {
                        ["inlineData"] = new JsonObject
                        {
                            ["mimeType"] = mimeType,
                            ["data"] = base64Data
                        }
                    });
                }
            }
            catch
            {
                // Failed to read file - skip
            }
        }
    }

    /// <summary>
    /// Process assistant message with thinking and tool call support.
    /// </summary>
    private static void ProcessAssistantMessage(JsonNode msg, JsonArray? contents, 
        bool includeThinking, string? globalThoughtSig, string modelName,
        Dictionary<string, string> tcIdToName, Dictionary<string, JsonNode?> toolResponses,
        Dictionary<string, JsonNode> toolNameToSchema, ISignatureCache? signatureCache)
    {
        var content = JsonHelper.GetPath(msg, "content");
        var node = new JsonObject
        {
            ["role"] = "model",
            ["parts"] = new JsonArray()
        };
        var parts = node["parts"] as JsonArray;

        // Task 11.2.4: Handle reasoning_content field
        var reasoningContent = JsonHelper.GetString(msg, "reasoning_content");
        if (!string.IsNullOrEmpty(reasoningContent))
        {
            var thoughtPart = new JsonObject
            {
                ["text"] = reasoningContent,
                ["thought"] = true
            };
            
            // Add signature if available
            if (!string.IsNullOrEmpty(globalThoughtSig))
            {
                thoughtPart["thoughtSignature"] = globalThoughtSig;
            }
            
            parts?.Add(thoughtPart);
        }
        else if (includeThinking)
        {
            // Task 11.2.3: Inject placeholder thinking block
            var thoughtPart = new JsonObject
            {
                ["text"] = "Applying tool decisions and generating response...",
                ["thought"] = true
            };
            
            if (!string.IsNullOrEmpty(globalThoughtSig))
            {
                thoughtPart["thoughtSignature"] = globalThoughtSig;
            }
            else if (modelName.ToLowerInvariant().Contains("gemini"))
            {
                thoughtPart["thoughtSignature"] = SkipThoughtSignatureValidator;
            }
            
            parts?.Add(thoughtPart);
        }

        // Process text content
        if (content is JsonValue contentVal)
        {
            try
            {
                var text = contentVal.GetValue<string>();
                if (!string.IsNullOrEmpty(text))
                {
                    parts?.Add(new JsonObject { ["text"] = text });
                }
            }
            catch { }
        }
        else if (content is JsonArray contentArr)
        {
            foreach (var item in contentArr)
            {
                var type = JsonHelper.GetString(item, "type");
                if (type == "text")
                {
                    var text = JsonHelper.GetString(item, "text") ?? "";
                    if (!string.IsNullOrEmpty(text))
                        parts?.Add(new JsonObject { ["text"] = text });
                }
            }
        }

        // Process tool calls
        var toolCalls = JsonHelper.GetPath(msg, "tool_calls") as JsonArray;
        if (toolCalls != null)
        {
            var funcIds = new List<string>();
            foreach (var tc in toolCalls)
            {
                if (JsonHelper.GetString(tc, "type") != "function") continue;

                var fid = JsonHelper.GetString(tc, "id") ?? "";
                var fname = JsonHelper.GetString(tc, "function.name") ?? "";
                var fargsRaw = JsonHelper.GetPath(tc, "function.arguments");

                // Task 11.3.6: Handle local_shell_call → shell remapping
                var finalName = fname == "local_shell_call" ? "shell" : fname;

                var fcPart = new JsonObject
                {
                    ["functionCall"] = new JsonObject
                    {
                        ["id"] = fid,
                        ["name"] = finalName
                    }
                };

                // Task 11.4.3: Add thoughtSignature to functionCall parts
                if (!string.IsNullOrEmpty(globalThoughtSig))
                {
                    fcPart["thoughtSignature"] = globalThoughtSig;
                }
                else if (includeThinking)
                {
                    fcPart["thoughtSignature"] = SkipThoughtSignatureValidator;
                }

                // Parse function arguments
                JsonObject? argsObj = null;
                if (fargsRaw is JsonValue fargsValue)
                {
                    try
                    {
                        var fargsStr = fargsValue.GetValue<string>();
                        if (!string.IsNullOrEmpty(fargsStr))
                        {
                            argsObj = JsonHelper.Parse(fargsStr) as JsonObject;
                        }
                    }
                    catch { }
                }
                else if (fargsRaw is JsonObject fargsObject)
                {
                    argsObj = fargsObject.DeepClone() as JsonObject;
                }

                argsObj ??= new JsonObject();

                // Task 11.3.2: Fix tool call args using schema
                if (toolNameToSchema.TryGetValue(fname, out var schema))
                {
                    FixToolCallArgs(argsObj, schema);
                }

                // Task 11.3.3: Clean JSON schema from args
                CleanJsonSchema(argsObj);

                (fcPart["functionCall"] as JsonObject)!["args"] = argsObj;
                parts?.Add(fcPart);
                
                if (!string.IsNullOrEmpty(fid))
                    funcIds.Add(fid);
            }

            contents?.Add(node);

            // Add tool responses
            if (funcIds.Count > 0)
            {
                var toolNode = new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray()
                };
                var toolParts = toolNode["parts"] as JsonArray;

                foreach (var fid in funcIds)
                {
                    if (tcIdToName.TryGetValue(fid, out var name))
                    {
                        JsonNode? respNode = null;
                        if (toolResponses.TryGetValue(fid, out var respContent))
                        {
                            if (respContent is JsonValue respValue)
                            {
                                try
                                {
                                    var respStr = respValue.GetValue<string>();
                                    respNode = JsonHelper.Parse(respStr) ?? JsonValue.Create(respStr);
                                }
                                catch { }
                            }
                            else
                            {
                                respNode = respContent?.DeepClone();
                            }
                        }

                        toolParts?.Add(new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["id"] = fid,
                                ["name"] = name,
                                ["response"] = new JsonObject
                                {
                                    ["result"] = respNode ?? new JsonObject()
                                }
                            }
                        });
                    }
                }

                if (toolParts?.Count > 0)
                    contents?.Add(toolNode);
            }
        }
        else
        {
            if (parts?.Count > 0)
                contents?.Add(node);
        }
    }

    /// <summary>
    /// Process tools with schema cleaning and type enforcement.
    /// Task 11.3.3, 11.3.4, 11.3.5, 11.3.6
    /// </summary>
    private static void ProcessTools(JsonNode root, JsonObject output)
    {
        var tools = JsonHelper.GetPath(root, "tools") as JsonArray;
        if (tools == null || tools.Count == 0) return;

        var request = output["request"] as JsonObject;
        var functionDeclarations = new JsonArray();

        foreach (var tool in tools)
        {
            if (JsonHelper.GetString(tool, "type") != "function") continue;

            var fn = JsonHelper.GetPath(tool, "function");
            if (fn == null) continue;

            var name = JsonHelper.GetString(fn, "name") ?? "";
            
            // Skip built-in search tools
            if (name == "web_search" || name == "google_search" || name == "web_search_20250305")
                continue;

            // Task 11.3.6: Handle local_shell_call → shell remapping
            var finalName = name == "local_shell_call" ? "shell" : name;

            var funcDecl = new JsonObject
            {
                ["name"] = finalName,
                ["description"] = JsonHelper.GetString(fn, "description") ?? ""
            };

            // Task 11.3.3: Clean and process parameters
            var parameters = JsonHelper.GetPath(fn, "parameters")?.DeepClone();
            if (parameters != null)
            {
                // Remove invalid fields
                CleanJsonSchema(parameters);
                
                // Ensure root has type
                if (parameters is JsonObject paramsObj && !paramsObj.ContainsKey("type"))
                {
                    paramsObj["type"] = "OBJECT";
                }
                
                // Task 11.3.4: Enforce uppercase types
                EnforceUppercaseTypes(parameters);
                
                funcDecl["parameters"] = parameters;
            }
            else
            {
                // Task 11.3.5: Inject default schema for tools without parameters
                funcDecl["parameters"] = new JsonObject
                {
                    ["type"] = "OBJECT",
                    ["properties"] = new JsonObject
                    {
                        ["content"] = new JsonObject
                        {
                            ["type"] = "STRING",
                            ["description"] = "The raw content or patch to be applied"
                        }
                    },
                    ["required"] = new JsonArray { "content" }
                };
            }

            functionDeclarations.Add(funcDecl);
        }

        // Handle google_search passthrough
        var hasGoogleSearch = false;
        foreach (var tool in tools)
        {
            var googleSearch = JsonHelper.GetPath(tool, "google_search");
            if (googleSearch != null)
            {
                hasGoogleSearch = true;
                break;
            }
        }

        if (functionDeclarations.Count > 0 || hasGoogleSearch)
        {
            var toolsNode = new JsonObject();
            
            if (functionDeclarations.Count > 0)
            {
                toolsNode["functionDeclarations"] = functionDeclarations;
            }
            
            if (hasGoogleSearch)
            {
                toolsNode["googleSearch"] = new JsonObject();
            }

            request!["tools"] = new JsonArray { toolsNode };
        }
    }

    private static void AddSafetySettings(JsonObject output)
    {
        var request = output["request"] as JsonObject;
        request!["safetySettings"] = new JsonArray
        {
            new JsonObject { ["category"] = "HARM_CATEGORY_HARASSMENT", ["threshold"] = "OFF" },
            new JsonObject { ["category"] = "HARM_CATEGORY_HATE_SPEECH", ["threshold"] = "OFF" },
            new JsonObject { ["category"] = "HARM_CATEGORY_SEXUALLY_EXPLICIT", ["threshold"] = "OFF" },
            new JsonObject { ["category"] = "HARM_CATEGORY_DANGEROUS_CONTENT", ["threshold"] = "OFF" },
            new JsonObject { ["category"] = "HARM_CATEGORY_CIVIC_INTEGRITY", ["threshold"] = "OFF" }
        };
    }
}
