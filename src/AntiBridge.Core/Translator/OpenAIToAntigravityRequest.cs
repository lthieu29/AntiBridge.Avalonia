using System.Text.Json.Nodes;

namespace AntiBridge.Core.Translator;

/// <summary>
/// Converts OpenAI API requests to Antigravity API format
/// Ported from CLIProxyAPI/internal/translator/antigravity/openai/chat-completions/antigravity_openai_request.go
/// </summary>
public static class OpenAIToAntigravityRequest
{
    private const string SkipThoughtSignatureValidator = "skip_thought_signature_validator";

    /// <summary>
    /// Convert OpenAI Chat Completions request to Antigravity format
    /// </summary>
    public static byte[] Convert(string modelName, byte[] rawJson, bool stream)
    {
        var root = JsonHelper.Parse(rawJson);
        if (root == null) return rawJson;

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

        // Process generation config
        ProcessGenerationConfig(root, output);

        // Process messages
        ProcessMessages(root, output);

        // Process tools
        ProcessTools(root, output);

        // Add safety settings
        AddSafetySettings(output);

        return System.Text.Encoding.UTF8.GetBytes(JsonHelper.Stringify(output));
    }

    private static void ProcessGenerationConfig(JsonNode root, JsonObject output)
    {
        var request = output["request"] as JsonObject;
        var genConfig = new JsonObject();
        var hasConfig = false;

        // Reasoning effort -> thinking config
        var reasoningEffort = JsonHelper.GetString(root, "reasoning_effort")?.ToLower().Trim();
        if (!string.IsNullOrEmpty(reasoningEffort))
        {
            var thinkingConfig = new JsonObject();
            if (reasoningEffort == "auto")
            {
                thinkingConfig["thinkingBudget"] = -1;
                thinkingConfig["includeThoughts"] = true;
            }
            else
            {
                thinkingConfig["thinkingLevel"] = reasoningEffort;
                thinkingConfig["includeThoughts"] = reasoningEffort != "none";
            }
            genConfig["thinkingConfig"] = thinkingConfig;
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

        if (hasConfig)
        {
            request!["generationConfig"] = genConfig;
        }
    }

    private static void ProcessMessages(JsonNode root, JsonObject output)
    {
        var messages = JsonHelper.GetPath(root, "messages") as JsonArray;
        if (messages == null) return;

        var request = output["request"] as JsonObject;
        var contents = request!["contents"] as JsonArray;

        // First pass: build tool call ID to name map
        var tcIdToName = new Dictionary<string, string>();
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
                                tcIdToName[id] = name;
                            }
                        }
                    }
                }
            }
        }

        // Second pass: build tool responses cache
        var toolResponses = new Dictionary<string, string>();
        foreach (var msg in messages)
        {
            var role = JsonHelper.GetString(msg, "role");
            if (role == "tool")
            {
                var toolCallId = JsonHelper.GetString(msg, "tool_call_id") ?? "";
                if (!string.IsNullOrEmpty(toolCallId))
                {
                    var content = JsonHelper.GetPath(msg, "content");
                    toolResponses[toolCallId] = content != null ? JsonHelper.Stringify(content) : "{}";
                }
            }
        }

        var systemPartIndex = 0;

        foreach (var msg in messages)
        {
            var role = JsonHelper.GetString(msg, "role");
            var content = JsonHelper.GetPath(msg, "content");

            if ((role == "system" || role == "developer") && messages.Count > 1)
            {
                // System instruction
                if (!JsonHelper.Exists(request, "systemInstruction"))
                {
                    request["systemInstruction"] = new JsonObject
                    {
                        ["role"] = "user",
                        ["parts"] = new JsonArray()
                    };
                }

                var sysParts = (request["systemInstruction"] as JsonObject)?["parts"] as JsonArray;

                if (content is JsonValue)
                {
                    sysParts?.Add(new JsonObject { ["text"] = content.GetValue<string>() });
                    systemPartIndex++;
                }
                else if (content is JsonArray contentArr)
                {
                    foreach (var item in contentArr)
                    {
                        var text = JsonHelper.GetString(item, "text") ?? "";
                        sysParts?.Add(new JsonObject { ["text"] = text });
                        systemPartIndex++;
                    }
                }
            }
            else if (role == "user" || ((role == "system" || role == "developer") && messages.Count == 1))
            {
                var node = new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray()
                };
                var parts = node["parts"] as JsonArray;

                if (content is JsonValue)
                {
                    parts?.Add(new JsonObject { ["text"] = content.GetValue<string>() });
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
                                var imageUrl = JsonHelper.GetString(item, "image_url.url") ?? "";
                                if (imageUrl.StartsWith("data:") && imageUrl.Length > 5)
                                {
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
                                                ["mime_type"] = mime,
                                                ["data"] = data
                                            },
                                            ["thoughtSignature"] = SkipThoughtSignatureValidator
                                        });
                                    }
                                }
                                break;
                        }
                    }
                }

                if (parts?.Count > 0)
                    contents?.Add(node);
            }
            else if (role == "assistant")
            {
                var node = new JsonObject
                {
                    ["role"] = "model",
                    ["parts"] = new JsonArray()
                };
                var parts = node["parts"] as JsonArray;

                if (content is JsonValue && !string.IsNullOrEmpty(content.GetValue<string>()))
                {
                    parts?.Add(new JsonObject { ["text"] = content.GetValue<string>() });
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

                // Tool calls
                var toolCalls = JsonHelper.GetPath(msg, "tool_calls") as JsonArray;
                if (toolCalls != null)
                {
                    var funcIds = new List<string>();
                    foreach (var tc in toolCalls)
                    {
                        if (JsonHelper.GetString(tc, "type") != "function") continue;

                        var fid = JsonHelper.GetString(tc, "id") ?? "";
                        var fname = JsonHelper.GetString(tc, "function.name") ?? "";
                        var fargs = JsonHelper.GetString(tc, "function.arguments") ?? "{}";

                        var fcPart = new JsonObject
                        {
                            ["functionCall"] = new JsonObject
                            {
                                ["id"] = fid,
                                ["name"] = fname
                            },
                            ["thoughtSignature"] = SkipThoughtSignatureValidator
                        };

                        // Parse args
                        var argsNode = JsonHelper.Parse(fargs);
                        if (argsNode != null)
                            (fcPart["functionCall"] as JsonObject)!["args"] = argsNode;
                        else
                            (fcPart["functionCall"] as JsonObject)!["args"] = new JsonObject { ["params"] = fargs };

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
                                var resp = toolResponses.GetValueOrDefault(fid, "{}");
                                var respNode = JsonHelper.Parse(resp);

                                var frPart = new JsonObject
                                {
                                    ["functionResponse"] = new JsonObject
                                    {
                                        ["id"] = fid,
                                        ["name"] = name,
                                        ["response"] = new JsonObject
                                        {
                                            ["result"] = respNode ?? JsonValue.Create(resp)
                                        }
                                    }
                                };
                                toolParts?.Add(frPart);
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
        }
    }

    private static void ProcessTools(JsonNode root, JsonObject output)
    {
        var tools = JsonHelper.GetPath(root, "tools") as JsonArray;
        if (tools == null || tools.Count == 0) return;

        var request = output["request"] as JsonObject;
        var toolNode = new JsonObject();
        var hasFunction = false;

        foreach (var tool in tools)
        {
            if (JsonHelper.GetString(tool, "type") == "function")
            {
                var fn = JsonHelper.GetPath(tool, "function");
                if (fn == null) continue;

                if (!hasFunction)
                {
                    toolNode["functionDeclarations"] = new JsonArray();
                    hasFunction = true;
                }

                var funcDecl = new JsonObject
                {
                    ["name"] = JsonHelper.GetString(fn, "name"),
                    ["description"] = JsonHelper.GetString(fn, "description")
                };

                var parameters = JsonHelper.GetPath(fn, "parameters");
                if (parameters != null)
                {
                    funcDecl["parametersJsonSchema"] = parameters.DeepClone();
                }
                else
                {
                    funcDecl["parametersJsonSchema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject()
                    };
                }

                (toolNode["functionDeclarations"] as JsonArray)?.Add(funcDecl);
            }

            // Google search passthrough
            var googleSearch = JsonHelper.GetPath(tool, "google_search");
            if (googleSearch != null)
            {
                toolNode["googleSearch"] = googleSearch.DeepClone();
            }
        }

        if (hasFunction || toolNode.ContainsKey("googleSearch"))
        {
            request!["tools"] = new JsonArray { toolNode };
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
