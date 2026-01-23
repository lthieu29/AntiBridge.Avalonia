using System.Text;
using System.Text.Json.Nodes;
using AntiBridge.Core.Services;
using AntiBridge.Core.Translator;
using NUnit.Framework;

namespace AntiBridge.Tests;

/// <summary>
/// Tests for Claude protocol translator fixes.
/// Validates Requirements 2.1-2.8 from the requirements document.
/// </summary>
[TestFixture]
public class ClaudeTranslatorTests
{
    #region 6.3.1 Test thinking block round-trip

    /// <summary>
    /// Test that thinking blocks are correctly extracted with signature handling.
    /// Validates Requirement 2.1: Thinking block extraction with signature handling.
    /// </summary>
    [Test]
    public void ThinkingBlock_WithSignature_ExtractsCorrectly()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "claude-3-5-sonnet",
            ["max_tokens"] = 1024,
            ["thinking"] = new JsonObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = 10000
            },
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "thinking",
                            ["thinking"] = "Let me think about this...",
                            ["signature"] = "claude#abc123signature456"
                        }
                    }
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var parts = result?["request"]?["contents"]?[0]?["parts"] as JsonArray;
        Assert.That(parts, Is.Not.Null);
        Assert.That(parts!.Count, Is.EqualTo(1));

        var thinkingPart = parts[0];
        Assert.That(JsonHelper.GetBool(thinkingPart, "thought"), Is.True);
        Assert.That(JsonHelper.GetString(thinkingPart, "text"), Is.EqualTo("Let me think about this..."));
        // Signature should have model prefix stripped
        Assert.That(JsonHelper.GetString(thinkingPart, "thoughtSignature"), Is.EqualTo("abc123signature456"));
    }

    /// <summary>
    /// Test that thinking blocks without model prefix in signature are handled correctly.
    /// </summary>
    [Test]
    public void ThinkingBlock_WithoutModelPrefix_PreservesSignature()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "claude-3-5-sonnet",
            ["max_tokens"] = 1024,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "thinking",
                            ["thinking"] = "Thinking content",
                            ["signature"] = "rawsignaturewithoutprefix"
                        }
                    }
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var parts = result?["request"]?["contents"]?[0]?["parts"] as JsonArray;
        Assert.That(parts, Is.Not.Null);
        var thinkingPart = parts![0];
        Assert.That(JsonHelper.GetString(thinkingPart, "thoughtSignature"), Is.EqualTo("rawsignaturewithoutprefix"));
    }

    #endregion

    #region 6.3.2 Test tool_use/tool_result mapping

    /// <summary>
    /// Test that tool_use blocks are correctly mapped with ID preservation.
    /// Validates Requirement 2.2: Tool_use block mapping with ID preservation.
    /// </summary>
    [Test]
    public void ToolUse_WithId_PreservesIdAndMapsCorrectly()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "claude-3-5-sonnet",
            ["max_tokens"] = 1024,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = "toolu_01ABC123",
                            ["name"] = "get_weather",
                            ["input"] = new JsonObject
                            {
                                ["location"] = "San Francisco",
                                ["unit"] = "celsius"
                            }
                        }
                    }
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var parts = result?["request"]?["contents"]?[0]?["parts"] as JsonArray;
        Assert.That(parts, Is.Not.Null);
        Assert.That(parts!.Count, Is.EqualTo(1));

        var toolPart = parts[0];
        var functionCall = toolPart?["functionCall"];
        Assert.That(functionCall, Is.Not.Null);
        Assert.That(JsonHelper.GetString(functionCall, "id"), Is.EqualTo("toolu_01ABC123"));
        Assert.That(JsonHelper.GetString(functionCall, "name"), Is.EqualTo("get_weather"));
        Assert.That(JsonHelper.GetString(functionCall, "args.location"), Is.EqualTo("San Francisco"));
        Assert.That(JsonHelper.GetString(functionCall, "args.unit"), Is.EqualTo("celsius"));
    }

    /// <summary>
    /// Test that tool_result blocks are correctly formatted with function response structure.
    /// Validates Requirement 2.3: Tool_result block formatting with function response structure.
    /// </summary>
    [Test]
    public void ToolResult_FormatsAsFunctionResponse()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "claude-3-5-sonnet",
            ["max_tokens"] = 1024,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = "get_weather-123-456",
                            ["content"] = "The weather in San Francisco is 72°F and sunny."
                        }
                    }
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var parts = result?["request"]?["contents"]?[0]?["parts"] as JsonArray;
        Assert.That(parts, Is.Not.Null);
        Assert.That(parts!.Count, Is.EqualTo(1));

        var responsePart = parts[0];
        var functionResponse = responsePart?["functionResponse"];
        Assert.That(functionResponse, Is.Not.Null);
        Assert.That(JsonHelper.GetString(functionResponse, "id"), Is.EqualTo("get_weather-123-456"));
        Assert.That(JsonHelper.GetString(functionResponse, "name"), Is.EqualTo("get_weather"));
        Assert.That(functionResponse?["response"]?["result"], Is.Not.Null);
    }

    /// <summary>
    /// Test tool_result with complex JSON content.
    /// </summary>
    [Test]
    public void ToolResult_WithJsonContent_PreservesStructure()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "claude-3-5-sonnet",
            ["max_tokens"] = 1024,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = "search-api-789",
                            ["content"] = new JsonObject
                            {
                                ["results"] = new JsonArray
                                {
                                    new JsonObject { ["title"] = "Result 1" },
                                    new JsonObject { ["title"] = "Result 2" }
                                },
                                ["total"] = 2
                            }
                        }
                    }
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var parts = result?["request"]?["contents"]?[0]?["parts"] as JsonArray;
        Assert.That(parts, Is.Not.Null);
        var functionResponse = parts![0]?["functionResponse"];
        var responseResult = functionResponse?["response"]?["result"];
        Assert.That(responseResult, Is.Not.Null);
        Assert.That(JsonHelper.GetInt(responseResult, "total"), Is.EqualTo(2));
    }

    #endregion

    #region 6.3.3 Test parts reordering

    /// <summary>
    /// Test that parts are reordered with thinking blocks first for model role.
    /// Validates Requirement 2.4: Parts reordering (thinking blocks first for model role).
    /// </summary>
    [Test]
    public void PartsReordering_ThinkingBlocksFirst_ForModelRole()
    {
        // Arrange - text comes before thinking in input
        var request = new JsonObject
        {
            ["model"] = "claude-3-5-sonnet",
            ["max_tokens"] = 1024,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = "Here is my response"
                        },
                        new JsonObject
                        {
                            ["type"] = "thinking",
                            ["thinking"] = "Let me think first...",
                            ["signature"] = "sig123"
                        }
                    }
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert - thinking should come first after reordering
        var parts = result?["request"]?["contents"]?[0]?["parts"] as JsonArray;
        Assert.That(parts, Is.Not.Null);
        Assert.That(parts!.Count, Is.EqualTo(2));

        // First part should be thinking (thought=true)
        Assert.That(JsonHelper.GetBool(parts[0], "thought"), Is.True);
        Assert.That(JsonHelper.GetString(parts[0], "text"), Is.EqualTo("Let me think first..."));

        // Second part should be text (thought=false or not present)
        Assert.That(JsonHelper.GetBool(parts[1], "thought"), Is.Not.True);
        Assert.That(JsonHelper.GetString(parts[1], "text"), Is.EqualTo("Here is my response"));
    }

    /// <summary>
    /// Test that multiple thinking blocks maintain their relative order.
    /// </summary>
    [Test]
    public void PartsReordering_MultipleThinkingBlocks_PreservesRelativeOrder()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "claude-3-5-sonnet",
            ["max_tokens"] = 1024,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = "Text 1" },
                        new JsonObject { ["type"] = "thinking", ["thinking"] = "Thinking A", ["signature"] = "sigA" },
                        new JsonObject { ["type"] = "text", ["text"] = "Text 2" },
                        new JsonObject { ["type"] = "thinking", ["thinking"] = "Thinking B", ["signature"] = "sigB" }
                    }
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert - thinking blocks first (A, B), then text blocks (1, 2)
        var parts = result?["request"]?["contents"]?[0]?["parts"] as JsonArray;
        Assert.That(parts, Is.Not.Null);
        Assert.That(parts!.Count, Is.EqualTo(4));

        // Thinking blocks first, in original order
        Assert.That(JsonHelper.GetBool(parts[0], "thought"), Is.True);
        Assert.That(JsonHelper.GetString(parts[0], "text"), Is.EqualTo("Thinking A"));
        Assert.That(JsonHelper.GetBool(parts[1], "thought"), Is.True);
        Assert.That(JsonHelper.GetString(parts[1], "text"), Is.EqualTo("Thinking B"));

        // Text blocks after, in original order
        Assert.That(JsonHelper.GetString(parts[2], "text"), Is.EqualTo("Text 1"));
        Assert.That(JsonHelper.GetString(parts[3], "text"), Is.EqualTo("Text 2"));
    }

    /// <summary>
    /// Test that user role messages are not reordered.
    /// </summary>
    [Test]
    public void PartsReordering_UserRole_NoReordering()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "claude-3-5-sonnet",
            ["max_tokens"] = 1024,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = "First text" },
                        new JsonObject { ["type"] = "text", ["text"] = "Second text" }
                    }
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert - order should be preserved for user role
        var parts = result?["request"]?["contents"]?[0]?["parts"] as JsonArray;
        Assert.That(parts, Is.Not.Null);
        Assert.That(parts!.Count, Is.EqualTo(2));
        Assert.That(JsonHelper.GetString(parts[0], "text"), Is.EqualTo("First text"));
        Assert.That(JsonHelper.GetString(parts[1], "text"), Is.EqualTo("Second text"));
    }

    #endregion

    #region 6.3.4 Test streaming event sequence

    /// <summary>
    /// Test that streaming responses emit correct SSE event sequence.
    /// Validates Requirement 2.6: SSE event emission sequence.
    /// </summary>
    [Test]
    public void StreamingResponse_EmitsCorrectEventSequence()
    {
        // Arrange
        var state = new ClaudeStreamState();
        
        // First chunk with text content
        var chunk1 = new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["responseId"] = "resp_123",
                ["modelVersion"] = "test-model",
                ["candidates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["content"] = new JsonObject
                        {
                            ["parts"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["text"] = "Hello, "
                                }
                            }
                        }
                    }
                }
            }
        };

        var chunk1Bytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(chunk1));

        // Act
        var results1 = AntigravityToClaudeResponse.ConvertStream("test-model", chunk1Bytes, state, null);

        // Assert - should have message_start, content_block_start, content_block_delta
        Assert.That(results1.Count, Is.EqualTo(1));
        var output1 = results1[0];
        Assert.That(output1, Does.Contain("event: message_start"));
        Assert.That(output1, Does.Contain("event: content_block_start"));
        Assert.That(output1, Does.Contain("event: content_block_delta"));
        Assert.That(output1, Does.Contain("\"type\":\"text_delta\""));
    }

    /// <summary>
    /// Test that thinking blocks emit signature_delta events with model group prefix.
    /// Validates Requirement 2.7: Signature delta event with model group prefix.
    /// </summary>
    [Test]
    public void StreamingResponse_ThinkingWithSignature_EmitsSignatureDelta()
    {
        // Arrange
        var state = new ClaudeStreamState();
        
        // First chunk - start thinking block
        var chunk1 = new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["responseId"] = "resp_123",
                ["candidates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["content"] = new JsonObject
                        {
                            ["parts"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["thought"] = true,
                                    ["text"] = "Let me think..."
                                }
                            }
                        }
                    }
                }
            }
        };

        var chunk1Bytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(chunk1));
        AntigravityToClaudeResponse.ConvertStream("claude-3-5-sonnet", chunk1Bytes, state, null);

        // Second chunk - signature
        var chunk2 = new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["candidates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["content"] = new JsonObject
                        {
                            ["parts"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["thought"] = true,
                                    ["text"] = "",
                                    ["thoughtSignature"] = "abc123signature"
                                }
                            }
                        }
                    }
                }
            }
        };

        var chunk2Bytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(chunk2));

        // Act
        var results2 = AntigravityToClaudeResponse.ConvertStream("claude-3-5-sonnet", chunk2Bytes, state, null);

        // Assert - should have signature_delta with model prefix
        Assert.That(results2.Count, Is.EqualTo(1));
        var output2 = results2[0];
        Assert.That(output2, Does.Contain("event: content_block_delta"));
        Assert.That(output2, Does.Contain("\"type\":\"signature_delta\""));
        Assert.That(output2, Does.Contain("claude#abc123signature"));
    }

    /// <summary>
    /// Test that tool_use responses generate unique IDs.
    /// Validates Requirement 2.8: Tool_use ID generation with unique IDs.
    /// </summary>
    [Test]
    public void StreamingResponse_ToolUse_GeneratesUniqueIds()
    {
        // Arrange
        var state = new ClaudeStreamState();
        
        var chunk = new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["responseId"] = "resp_123",
                ["candidates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["content"] = new JsonObject
                        {
                            ["parts"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["functionCall"] = new JsonObject
                                    {
                                        ["name"] = "get_weather",
                                        ["args"] = new JsonObject
                                        {
                                            ["location"] = "NYC"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var chunkBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(chunk));

        // Act
        var results = AntigravityToClaudeResponse.ConvertStream("test-model", chunkBytes, state, null);

        // Assert
        Assert.That(results.Count, Is.EqualTo(1));
        var output = results[0];
        Assert.That(output, Does.Contain("event: content_block_start"));
        Assert.That(output, Does.Contain("\"type\":\"tool_use\""));
        Assert.That(output, Does.Contain("\"name\":\"get_weather\""));
        // ID should contain function name and be unique
        Assert.That(output, Does.Contain("get_weather-"));
    }

    /// <summary>
    /// Test that function call input JSON is correctly formatted.
    /// Validates Requirement 2.8: Input JSON formatting for function calls.
    /// </summary>
    [Test]
    public void StreamingResponse_ToolUse_FormatsInputJsonCorrectly()
    {
        // Arrange
        var state = new ClaudeStreamState();
        
        var chunk = new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["responseId"] = "resp_123",
                ["candidates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["content"] = new JsonObject
                        {
                            ["parts"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["functionCall"] = new JsonObject
                                    {
                                        ["name"] = "search",
                                        ["args"] = new JsonObject
                                        {
                                            ["query"] = "test query",
                                            ["limit"] = 10,
                                            ["filters"] = new JsonObject
                                            {
                                                ["type"] = "article"
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var chunkBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(chunk));

        // Act
        var results = AntigravityToClaudeResponse.ConvertStream("test-model", chunkBytes, state, null);

        // Assert
        Assert.That(results.Count, Is.EqualTo(1));
        var output = results[0];
        Assert.That(output, Does.Contain("\"type\":\"input_json_delta\""));
        // The partial_json contains the JSON args - check for key parts
        Assert.That(output, Does.Contain("query"));
        Assert.That(output, Does.Contain("test query"));
        Assert.That(output, Does.Contain("limit"));
    }

    /// <summary>
    /// Test complete streaming sequence with message_stop.
    /// </summary>
    [Test]
    public void StreamingResponse_Complete_EmitsMessageStop()
    {
        // Arrange
        var state = new ClaudeStreamState();
        
        // First chunk with content
        var chunk1 = new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["responseId"] = "resp_123",
                ["candidates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["content"] = new JsonObject
                        {
                            ["parts"] = new JsonArray
                            {
                                new JsonObject { ["text"] = "Hello" }
                            }
                        }
                    }
                }
            }
        };
        AntigravityToClaudeResponse.ConvertStream("test-model", Encoding.UTF8.GetBytes(JsonHelper.Stringify(chunk1)), state, null);

        // Final chunk with finish reason and usage
        var chunk2 = new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["candidates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["finishReason"] = "STOP"
                    }
                },
                ["usageMetadata"] = new JsonObject
                {
                    ["promptTokenCount"] = 100,
                    ["candidatesTokenCount"] = 50,
                    ["totalTokenCount"] = 150
                }
            }
        };
        AntigravityToClaudeResponse.ConvertStream("test-model", Encoding.UTF8.GetBytes(JsonHelper.Stringify(chunk2)), state, null);

        // Act - send [DONE] marker
        var doneResults = AntigravityToClaudeResponse.ConvertStream("test-model", Encoding.UTF8.GetBytes("[DONE]"), state, null);

        // Assert
        Assert.That(doneResults.Count, Is.EqualTo(1));
        var output = doneResults[0];
        Assert.That(output, Does.Contain("event: message_stop"));
        Assert.That(output, Does.Contain("\"type\":\"message_stop\""));
    }

    #endregion

    #region Interleaved Thinking Hint Tests

    /// <summary>
    /// Test that interleaved thinking hint is injected when both tools and thinking are enabled.
    /// Validates Requirement 2.5: Interleaved thinking hint injection.
    /// </summary>
    [Test]
    public void InterleavedThinkingHint_ToolsAndThinkingEnabled_InjectsHint()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "claude-3-5-sonnet",
            ["max_tokens"] = 1024,
            ["thinking"] = new JsonObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = 10000
            },
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "get_weather",
                    ["description"] = "Get weather info",
                    ["input_schema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject()
                    }
                }
            },
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "What's the weather?"
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert - system instruction should contain the hint
        var systemParts = result?["request"]?["systemInstruction"]?["parts"] as JsonArray;
        Assert.That(systemParts, Is.Not.Null);
        
        var hasHint = false;
        foreach (var part in systemParts!)
        {
            var text = JsonHelper.GetString(part, "text");
            if (text?.Contains("Interleaved thinking is enabled") == true)
            {
                hasHint = true;
                break;
            }
        }
        Assert.That(hasHint, Is.True, "Should inject interleaved thinking hint");
    }

    /// <summary>
    /// Test that hint is NOT injected when only tools are enabled (no thinking).
    /// </summary>
    [Test]
    public void InterleavedThinkingHint_OnlyToolsEnabled_NoHint()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "claude-3-5-sonnet",
            ["max_tokens"] = 1024,
            ["tools"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "get_weather",
                    ["description"] = "Get weather info",
                    ["input_schema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject()
                    }
                }
            },
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "What's the weather?"
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert - should not have system instruction with hint
        var systemInstruction = result?["request"]?["systemInstruction"];
        if (systemInstruction != null)
        {
            var systemParts = systemInstruction["parts"] as JsonArray;
            if (systemParts != null)
            {
                foreach (var part in systemParts)
                {
                    var text = JsonHelper.GetString(part, "text");
                    Assert.That(text?.Contains("Interleaved thinking is enabled"), Is.Not.True);
                }
            }
        }
    }

    /// <summary>
    /// Test that hint is NOT injected when only thinking is enabled (no tools).
    /// </summary>
    [Test]
    public void InterleavedThinkingHint_OnlyThinkingEnabled_NoHint()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "claude-3-5-sonnet",
            ["max_tokens"] = 1024,
            ["thinking"] = new JsonObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = 10000
            },
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "Hello"
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert - should not have system instruction with hint
        var systemInstruction = result?["request"]?["systemInstruction"];
        if (systemInstruction != null)
        {
            var systemParts = systemInstruction["parts"] as JsonArray;
            if (systemParts != null)
            {
                foreach (var part in systemParts)
                {
                    var text = JsonHelper.GetString(part, "text");
                    Assert.That(text?.Contains("Interleaved thinking is enabled"), Is.Not.True);
                }
            }
        }
    }

    #endregion

    #region Non-Streaming Response Tests

    /// <summary>
    /// Test non-streaming response conversion with thinking blocks.
    /// </summary>
    [Test]
    public void NonStreamingResponse_WithThinking_ConvertsCorrectly()
    {
        // Arrange
        var response = new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["responseId"] = "resp_123",
                ["modelVersion"] = "test-model",
                ["candidates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["content"] = new JsonObject
                        {
                            ["parts"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["thought"] = true,
                                    ["text"] = "Let me think about this...",
                                    ["thoughtSignature"] = "sig123"
                                },
                                new JsonObject
                                {
                                    ["text"] = "Here is my answer."
                                }
                            }
                        },
                        ["finishReason"] = "STOP"
                    }
                },
                ["usageMetadata"] = new JsonObject
                {
                    ["promptTokenCount"] = 100,
                    ["candidatesTokenCount"] = 50,
                    ["thoughtsTokenCount"] = 30,
                    ["totalTokenCount"] = 180
                }
            }
        };

        var responseBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(response));

        // Act
        var result = AntigravityToClaudeResponse.ConvertNonStream("claude-3-5-sonnet", responseBytes, null);
        var parsed = JsonNode.Parse(result);

        // Assert
        Assert.That(parsed, Is.Not.Null);
        Assert.That(JsonHelper.GetString(parsed, "type"), Is.EqualTo("message"));
        Assert.That(JsonHelper.GetString(parsed, "role"), Is.EqualTo("assistant"));
        Assert.That(JsonHelper.GetString(parsed, "stop_reason"), Is.EqualTo("end_turn"));

        var content = parsed?["content"] as JsonArray;
        Assert.That(content, Is.Not.Null);
        Assert.That(content!.Count, Is.EqualTo(2));

        // First should be thinking block
        Assert.That(JsonHelper.GetString(content[0], "type"), Is.EqualTo("thinking"));
        Assert.That(JsonHelper.GetString(content[0], "thinking"), Is.EqualTo("Let me think about this..."));
        Assert.That(JsonHelper.GetString(content[0], "signature"), Does.Contain("sig123"));

        // Second should be text block
        Assert.That(JsonHelper.GetString(content[1], "type"), Is.EqualTo("text"));
        Assert.That(JsonHelper.GetString(content[1], "text"), Is.EqualTo("Here is my answer."));
    }

    /// <summary>
    /// Test non-streaming response with tool use.
    /// </summary>
    [Test]
    public void NonStreamingResponse_WithToolUse_ConvertsCorrectly()
    {
        // Arrange
        var response = new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["responseId"] = "resp_123",
                ["candidates"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["content"] = new JsonObject
                        {
                            ["parts"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["functionCall"] = new JsonObject
                                    {
                                        ["name"] = "get_weather",
                                        ["args"] = new JsonObject
                                        {
                                            ["location"] = "NYC"
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                ["usageMetadata"] = new JsonObject
                {
                    ["promptTokenCount"] = 100,
                    ["candidatesTokenCount"] = 20,
                    ["totalTokenCount"] = 120
                }
            }
        };

        var responseBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(response));

        // Act
        var result = AntigravityToClaudeResponse.ConvertNonStream("test-model", responseBytes, null);
        var parsed = JsonNode.Parse(result);

        // Assert
        Assert.That(parsed, Is.Not.Null);
        Assert.That(JsonHelper.GetString(parsed, "stop_reason"), Is.EqualTo("tool_use"));

        var content = parsed?["content"] as JsonArray;
        Assert.That(content, Is.Not.Null);
        Assert.That(content!.Count, Is.EqualTo(1));

        var toolUse = content[0];
        Assert.That(JsonHelper.GetString(toolUse, "type"), Is.EqualTo("tool_use"));
        Assert.That(JsonHelper.GetString(toolUse, "name"), Is.EqualTo("get_weather"));
        Assert.That(JsonHelper.GetString(toolUse, "input.location"), Is.EqualTo("NYC"));
        // Should have a generated ID
        Assert.That(JsonHelper.GetString(toolUse, "id"), Is.Not.Null.And.Not.Empty);
    }

    #endregion

    #region Task 10.5 - Critical Fixes Tests

    #region 10.5.1 Test cache_control cleaning

    /// <summary>
    /// Test that cache_control fields are removed from thinking blocks.
    /// Validates Task 10.1.1: CleanCacheControlFromMessages()
    /// </summary>
    [Test]
    public void CleanCacheControl_RemovesFromThinkingBlocks()
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
                        ["thinking"] = "Let me think...",
                        ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
                    }
                }
            }
        };

        // Act
        var removed = ClaudeToAntigravityRequest.CleanCacheControlFromMessages(messages);

        // Assert
        Assert.That(removed, Is.EqualTo(1));
        var block = messages[0]?["content"]?[0] as JsonObject;
        Assert.That(block?.ContainsKey("cache_control"), Is.False);
    }

    /// <summary>
    /// Test that cache_control fields are removed from tool_use blocks.
    /// </summary>
    [Test]
    public void CleanCacheControl_RemovesFromToolUseBlocks()
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
                        ["type"] = "tool_use",
                        ["id"] = "tool_123",
                        ["name"] = "get_weather",
                        ["input"] = new JsonObject(),
                        ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
                    }
                }
            }
        };

        // Act
        var removed = ClaudeToAntigravityRequest.CleanCacheControlFromMessages(messages);

        // Assert
        Assert.That(removed, Is.EqualTo(1));
        var block = messages[0]?["content"]?[0] as JsonObject;
        Assert.That(block?.ContainsKey("cache_control"), Is.False);
    }

    /// <summary>
    /// Test deep cleaning of cache_control from nested JSON.
    /// </summary>
    [Test]
    public void DeepCleanCacheControl_RemovesFromNestedStructures()
    {
        // Arrange
        var json = new JsonObject
        {
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
                        }
                    },
                    ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
                }
            },
            ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
        };

        // Act
        var removed = JsonHelper.DeepCleanCacheControl(json);

        // Assert
        Assert.That(removed, Is.EqualTo(3));
        Assert.That(json.ContainsKey("cache_control"), Is.False);
    }

    #endregion

    #region 10.5.2 Test consecutive message merging

    /// <summary>
    /// Test that consecutive user messages are merged.
    /// Validates Task 10.1.2: MergeConsecutiveMessages()
    /// </summary>
    [Test]
    public void MergeConsecutiveMessages_MergesUserMessages()
    {
        // Arrange
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = "First message" }
                }
            },
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = "Second message" }
                }
            }
        };

        // Act
        var merged = ClaudeToAntigravityRequest.MergeConsecutiveMessages(messages);

        // Assert
        Assert.That(merged, Is.True);
        Assert.That(messages.Count, Is.EqualTo(1));
        var content = messages[0]?["content"] as JsonArray;
        Assert.That(content?.Count, Is.EqualTo(2));
    }

    /// <summary>
    /// Test that alternating role messages are not merged.
    /// </summary>
    [Test]
    public void MergeConsecutiveMessages_PreservesAlternatingRoles()
    {
        // Arrange
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "Hello" },
            new JsonObject { ["role"] = "assistant", ["content"] = "Hi there" },
            new JsonObject { ["role"] = "user", ["content"] = "How are you?" }
        };

        // Act
        var merged = ClaudeToAntigravityRequest.MergeConsecutiveMessages(messages);

        // Assert
        Assert.That(merged, Is.False);
        Assert.That(messages.Count, Is.EqualTo(3));
    }

    /// <summary>
    /// Test merging string content messages.
    /// </summary>
    [Test]
    public void MergeConsecutiveMessages_MergesStringContent()
    {
        // Arrange
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "First" },
            new JsonObject { ["role"] = "user", ["content"] = "Second" }
        };

        // Act
        var merged = ClaudeToAntigravityRequest.MergeConsecutiveMessages(messages);

        // Assert
        Assert.That(merged, Is.True);
        Assert.That(messages.Count, Is.EqualTo(1));
        var content = JsonHelper.GetString(messages[0], "content");
        Assert.That(content, Does.Contain("First"));
        Assert.That(content, Does.Contain("Second"));
    }

    #endregion

    #region 10.5.3 Test thinking block sorting

    /// <summary>
    /// Test that thinking blocks are sorted first in assistant messages.
    /// Validates Task 10.1.3: SortThinkingBlocksFirst()
    /// </summary>
    [Test]
    public void SortThinkingBlocksFirst_ReordersBlocks()
    {
        // Arrange
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = "Response text" },
                    new JsonObject { ["type"] = "thinking", ["thinking"] = "Let me think..." }
                }
            }
        };

        // Act
        var reordered = ClaudeToAntigravityRequest.SortThinkingBlocksFirst(messages);

        // Assert
        Assert.That(reordered, Is.True);
        var content = messages[0]?["content"] as JsonArray;
        Assert.That(JsonHelper.GetString(content?[0], "type"), Is.EqualTo("thinking"));
        Assert.That(JsonHelper.GetString(content?[1], "type"), Is.EqualTo("text"));
    }

    /// <summary>
    /// Test that user messages are not reordered.
    /// </summary>
    [Test]
    public void SortThinkingBlocksFirst_IgnoresUserMessages()
    {
        // Arrange
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "text", ["text"] = "First" },
                    new JsonObject { ["type"] = "text", ["text"] = "Second" }
                }
            }
        };

        // Act
        var reordered = ClaudeToAntigravityRequest.SortThinkingBlocksFirst(messages);

        // Assert
        Assert.That(reordered, Is.False);
    }

    /// <summary>
    /// Test triple-stage partition: Thinking -> Text -> ToolUse
    /// </summary>
    [Test]
    public void SortThinkingBlocksFirst_TripleStagePartition()
    {
        // Arrange
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "tool_use", ["id"] = "tool_1", ["name"] = "test", ["input"] = new JsonObject() },
                    new JsonObject { ["type"] = "text", ["text"] = "Response" },
                    new JsonObject { ["type"] = "thinking", ["thinking"] = "Thinking..." }
                }
            }
        };

        // Act
        var reordered = ClaudeToAntigravityRequest.SortThinkingBlocksFirst(messages);

        // Assert
        Assert.That(reordered, Is.True);
        var content = messages[0]?["content"] as JsonArray;
        Assert.That(JsonHelper.GetString(content?[0], "type"), Is.EqualTo("thinking"));
        Assert.That(JsonHelper.GetString(content?[1], "type"), Is.EqualTo("text"));
        Assert.That(JsonHelper.GetString(content?[2], "type"), Is.EqualTo("tool_use"));
    }

    #endregion

    #region 10.5.4 Test signature validation

    /// <summary>
    /// Test that valid signatures are recognized.
    /// Validates Task 10.2.1: MIN_SIGNATURE_LENGTH constant
    /// </summary>
    [Test]
    public void IsValidSignature_ValidatesLength()
    {
        // Arrange
        var shortSig = "short";
        var validSig = new string('a', 50);
        var longSig = new string('b', 100);

        // Act & Assert
        Assert.That(ClaudeToAntigravityRequest.IsValidSignature(shortSig), Is.False);
        Assert.That(ClaudeToAntigravityRequest.IsValidSignature(validSig), Is.True);
        Assert.That(ClaudeToAntigravityRequest.IsValidSignature(longSig), Is.True);
        Assert.That(ClaudeToAntigravityRequest.IsValidSignature(null), Is.False);
        Assert.That(ClaudeToAntigravityRequest.IsValidSignature(""), Is.False);
    }

    /// <summary>
    /// Test model compatibility checking.
    /// Validates Task 10.2.3: IsModelCompatible()
    /// </summary>
    [Test]
    public void IsModelCompatible_ChecksModelFamily()
    {
        // Arrange & Act & Assert
        Assert.That(ClaudeToAntigravityRequest.IsModelCompatible("claude#sig123", "claude-3-5-sonnet"), Is.True);
        Assert.That(ClaudeToAntigravityRequest.IsModelCompatible("gemini#sig123", "gemini-2.5-flash"), Is.True);
        Assert.That(ClaudeToAntigravityRequest.IsModelCompatible("antigravity#sig123", "any-model"), Is.True);
        Assert.That(ClaudeToAntigravityRequest.IsModelCompatible("claude#sig123", "gemini-2.5-flash"), Is.False);
        Assert.That(ClaudeToAntigravityRequest.IsModelCompatible("rawsignature", "any-model"), Is.True); // No prefix = compatible
    }

    /// <summary>
    /// Test ShouldDisableThinkingDueToHistory detection.
    /// Validates Task 10.1.4: ShouldDisableThinkingDueToHistory()
    /// </summary>
    [Test]
    public void ShouldDisableThinkingDueToHistory_DetectsToolUseWithoutThinking()
    {
        // Arrange - assistant message with tool_use but no thinking
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = "tool_123",
                        ["name"] = "get_weather",
                        ["input"] = new JsonObject()
                    }
                }
            }
        };

        // Act
        var shouldDisable = ClaudeToAntigravityRequest.ShouldDisableThinkingDueToHistory(messages);

        // Assert
        Assert.That(shouldDisable, Is.True);
    }

    /// <summary>
    /// Test that thinking with tool_use is compatible.
    /// </summary>
    [Test]
    public void ShouldDisableThinkingDueToHistory_AllowsThinkingWithToolUse()
    {
        // Arrange - assistant message with both thinking and tool_use
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray
                {
                    new JsonObject { ["type"] = "thinking", ["thinking"] = "Let me think..." },
                    new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = "tool_123",
                        ["name"] = "get_weather",
                        ["input"] = new JsonObject()
                    }
                }
            }
        };

        // Act
        var shouldDisable = ClaudeToAntigravityRequest.ShouldDisableThinkingDueToHistory(messages);

        // Assert
        Assert.That(shouldDisable, Is.False);
    }

    #endregion

    #region 10.5.5 Test Base64 signature decoding

    /// <summary>
    /// Test Base64 signature decoding.
    /// Validates Task 10.3.1: Base64 signature decoding
    /// </summary>
    [Test]
    public void DecodeBase64Signature_DecodesValidBase64()
    {
        // Arrange
        var originalSig = "This is a test signature for decoding";
        var base64Sig = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalSig));

        // Act
        var decoded = AntigravityToClaudeResponse.DecodeBase64Signature(base64Sig);

        // Assert
        Assert.That(decoded, Is.EqualTo(originalSig));
    }

    /// <summary>
    /// Test that non-Base64 signatures are returned as-is.
    /// </summary>
    [Test]
    public void DecodeBase64Signature_PreservesNonBase64()
    {
        // Arrange
        var rawSig = "not-base64-encoded-signature";

        // Act
        var result = AntigravityToClaudeResponse.DecodeBase64Signature(rawSig);

        // Assert
        Assert.That(result, Is.EqualTo(rawSig));
    }

    /// <summary>
    /// Test handling of null/empty signatures.
    /// </summary>
    [Test]
    public void DecodeBase64Signature_HandlesNullAndEmpty()
    {
        // Act & Assert
        Assert.That(AntigravityToClaudeResponse.DecodeBase64Signature(null), Is.EqualTo(""));
        Assert.That(AntigravityToClaudeResponse.DecodeBase64Signature(""), Is.EqualTo(""));
    }

    #endregion

    #region 10.5.6 Test function call args remapping

    /// <summary>
    /// Test grep tool argument remapping.
    /// Validates Task 10.3.2 & 10.3.3: RemapFunctionCallArgs()
    /// </summary>
    [Test]
    public void RemapFunctionCallArgs_RemapsGrepArgs()
    {
        // Arrange
        var args = new JsonObject
        {
            ["description"] = "search pattern",
            ["paths"] = new JsonArray { "/src" }
        };

        // Act
        AntigravityToClaudeResponse.RemapFunctionCallArgs("grep", args);

        // Assert
        Assert.That(JsonHelper.GetString(args, "pattern"), Is.EqualTo("search pattern"));
        Assert.That(JsonHelper.GetString(args, "path"), Is.EqualTo("/src"));
        Assert.That(args.ContainsKey("description"), Is.False);
        Assert.That(args.ContainsKey("paths"), Is.False);
    }

    /// <summary>
    /// Test query to pattern remapping.
    /// </summary>
    [Test]
    public void RemapFunctionCallArgs_RemapsQueryToPattern()
    {
        // Arrange
        var args = new JsonObject
        {
            ["query"] = "search term"
        };

        // Act
        AntigravityToClaudeResponse.RemapFunctionCallArgs("glob", args);

        // Assert
        Assert.That(JsonHelper.GetString(args, "pattern"), Is.EqualTo("search term"));
        Assert.That(args.ContainsKey("query"), Is.False);
    }

    /// <summary>
    /// Test read tool path remapping.
    /// </summary>
    [Test]
    public void RemapFunctionCallArgs_RemapsReadArgs()
    {
        // Arrange
        var args = new JsonObject
        {
            ["path"] = "/src/file.txt"
        };

        // Act
        AntigravityToClaudeResponse.RemapFunctionCallArgs("read", args);

        // Assert
        Assert.That(JsonHelper.GetString(args, "file_path"), Is.EqualTo("/src/file.txt"));
        Assert.That(args.ContainsKey("path"), Is.False);
    }

    /// <summary>
    /// Test ls tool default path.
    /// </summary>
    [Test]
    public void RemapFunctionCallArgs_AddsDefaultPathForLs()
    {
        // Arrange
        var args = new JsonObject();

        // Act
        AntigravityToClaudeResponse.RemapFunctionCallArgs("ls", args);

        // Assert
        Assert.That(JsonHelper.GetString(args, "path"), Is.EqualTo("."));
    }

    /// <summary>
    /// Test EnterPlanMode tool clears all args.
    /// Validates Task 10.3.4: EnterPlanMode special handling
    /// </summary>
    [Test]
    public void RemapFunctionCallArgs_ClearsEnterPlanModeArgs()
    {
        // Arrange
        var args = new JsonObject
        {
            ["some_arg"] = "value",
            ["another_arg"] = 123
        };

        // Act
        AntigravityToClaudeResponse.RemapFunctionCallArgs("EnterPlanMode", args);

        // Assert
        Assert.That(args.Count, Is.EqualTo(0));
    }

    /// <summary>
    /// Test generic paths to path remapping.
    /// </summary>
    [Test]
    public void RemapFunctionCallArgs_GenericPathsRemapping()
    {
        // Arrange
        var args = new JsonObject
        {
            ["paths"] = new JsonArray { "/first/path", "/second/path" }
        };

        // Act
        AntigravityToClaudeResponse.RemapFunctionCallArgs("unknown_tool", args);

        // Assert
        Assert.That(JsonHelper.GetString(args, "path"), Is.EqualTo("/first/path"));
        Assert.That(args.ContainsKey("paths"), Is.False);
    }

    #endregion

    #region Deep Clean Undefined Tests

    /// <summary>
    /// Test removal of "[undefined]" strings from JSON.
    /// Validates Task 10.4.2: DeepCleanUndefined()
    /// </summary>
    [Test]
    public void DeepCleanUndefined_RemovesUndefinedStrings()
    {
        // Arrange
        var json = new JsonObject
        {
            ["valid"] = "value",
            ["undefined_field"] = "[undefined]",
            ["nested"] = new JsonObject
            {
                ["also_undefined"] = "[undefined]",
                ["valid_nested"] = "ok"
            }
        };

        // Act
        var cleaned = JsonHelper.DeepCleanUndefined(json);

        // Assert
        Assert.That(cleaned, Is.EqualTo(2));
        Assert.That(json.ContainsKey("undefined_field"), Is.False);
        Assert.That(json.ContainsKey("valid"), Is.True);
        var nested = json["nested"] as JsonObject;
        Assert.That(nested?.ContainsKey("also_undefined"), Is.False);
        Assert.That(nested?.ContainsKey("valid_nested"), Is.True);
    }

    /// <summary>
    /// Test removal of "[undefined]" from arrays.
    /// </summary>
    [Test]
    public void DeepCleanUndefined_RemovesFromArrays()
    {
        // Arrange
        var json = new JsonArray
        {
            "valid",
            "[undefined]",
            "also valid"
        };

        // Act
        var cleaned = JsonHelper.DeepCleanUndefined(json);

        // Assert
        Assert.That(cleaned, Is.EqualTo(1));
        Assert.That(json.Count, Is.EqualTo(2));
    }

    #endregion

    #endregion
}
