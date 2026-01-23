using System.Text;
using System.Text.Json.Nodes;
using AntiBridge.Core.Translator;
using NUnit.Framework;

namespace AntiBridge.Tests;

/// <summary>
/// Tests for OpenAI protocol translator fixes.
/// Validates Requirements 3.1-3.6 from the requirements document.
/// </summary>
[TestFixture]
public class OpenAITranslatorTests
{
    #region 7.3.1 Test reasoning_effort mapping

    /// <summary>
    /// Test that reasoning_effort "auto" maps to unlimited thinking budget.
    /// Validates Requirement 3.1: reasoning_effort parameter handling.
    /// </summary>
    [Test]
    public void ReasoningEffort_Auto_MapsToUnlimitedBudget()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "gpt-4o",
            ["reasoning_effort"] = "auto",
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
        var resultBytes = OpenAIToAntigravityRequest.Convert("test-model", requestBytes, false);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var thinkingConfig = result?["request"]?["generationConfig"]?["thinkingConfig"];
        Assert.That(thinkingConfig, Is.Not.Null);
        Assert.That(JsonHelper.GetInt(thinkingConfig, "thinkingBudget"), Is.EqualTo(-1));
        Assert.That(JsonHelper.GetBool(thinkingConfig, "includeThoughts"), Is.True);
    }

    /// <summary>
    /// Test that reasoning_effort "none" disables thinking.
    /// Validates Requirement 3.1: reasoning_effort parameter handling.
    /// </summary>
    [Test]
    public void ReasoningEffort_None_DisablesThinking()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "gpt-4o",
            ["reasoning_effort"] = "none",
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
        var resultBytes = OpenAIToAntigravityRequest.Convert("test-model", requestBytes, false);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var thinkingConfig = result?["request"]?["generationConfig"]?["thinkingConfig"];
        Assert.That(thinkingConfig, Is.Not.Null);
        Assert.That(JsonHelper.GetBool(thinkingConfig, "includeThoughts"), Is.False);
    }

    /// <summary>
    /// Test that reasoning_effort "low" maps to 1024 token budget.
    /// Validates Requirement 3.1: reasoning_effort parameter handling.
    /// </summary>
    [Test]
    public void ReasoningEffort_Low_MapsTo1024Budget()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "gpt-4o",
            ["reasoning_effort"] = "low",
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
        var resultBytes = OpenAIToAntigravityRequest.Convert("test-model", requestBytes, false);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var thinkingConfig = result?["request"]?["generationConfig"]?["thinkingConfig"];
        Assert.That(thinkingConfig, Is.Not.Null);
        Assert.That(JsonHelper.GetInt(thinkingConfig, "thinkingBudget"), Is.EqualTo(1024));
        Assert.That(JsonHelper.GetBool(thinkingConfig, "includeThoughts"), Is.True);
    }

    /// <summary>
    /// Test that reasoning_effort "medium" maps to 8192 token budget.
    /// Validates Requirement 3.1: reasoning_effort parameter handling.
    /// </summary>
    [Test]
    public void ReasoningEffort_Medium_MapsTo8192Budget()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "gpt-4o",
            ["reasoning_effort"] = "medium",
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
        var resultBytes = OpenAIToAntigravityRequest.Convert("test-model", requestBytes, false);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var thinkingConfig = result?["request"]?["generationConfig"]?["thinkingConfig"];
        Assert.That(thinkingConfig, Is.Not.Null);
        Assert.That(JsonHelper.GetInt(thinkingConfig, "thinkingBudget"), Is.EqualTo(8192));
        Assert.That(JsonHelper.GetBool(thinkingConfig, "includeThoughts"), Is.True);
    }

    /// <summary>
    /// Test that reasoning_effort "high" maps to 32768 token budget.
    /// Validates Requirement 3.1: reasoning_effort parameter handling.
    /// </summary>
    [Test]
    public void ReasoningEffort_High_MapsTo32768Budget()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "gpt-4o",
            ["reasoning_effort"] = "high",
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
        var resultBytes = OpenAIToAntigravityRequest.Convert("test-model", requestBytes, false);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var thinkingConfig = result?["request"]?["generationConfig"]?["thinkingConfig"];
        Assert.That(thinkingConfig, Is.Not.Null);
        Assert.That(JsonHelper.GetInt(thinkingConfig, "thinkingBudget"), Is.EqualTo(32768));
        Assert.That(JsonHelper.GetBool(thinkingConfig, "includeThoughts"), Is.True);
    }

    #endregion

    #region 7.3.2 Test tool_calls round-trip

    /// <summary>
    /// Test that tool_calls in assistant messages are correctly mapped with ID tracking.
    /// Validates Requirement 3.2: Tool_calls mapping with ID tracking.
    /// </summary>
    [Test]
    public void ToolCalls_InAssistantMessage_MapsCorrectlyWithIdTracking()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "gpt-4o",
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "What's the weather?"
                },
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = (JsonNode?)null,
                    ["tool_calls"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["id"] = "call_abc123",
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = "get_weather",
                                ["arguments"] = "{\"location\":\"San Francisco\",\"unit\":\"celsius\"}"
                            }
                        }
                    }
                },
                new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = "call_abc123",
                    ["content"] = "The weather is 72°F and sunny."
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = OpenAIToAntigravityRequest.Convert("test-model", requestBytes, false);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var contents = result?["request"]?["contents"] as JsonArray;
        Assert.That(contents, Is.Not.Null);
        Assert.That(contents!.Count, Is.GreaterThanOrEqualTo(2));

        // Check assistant message with function call
        var assistantContent = contents[1];
        Assert.That(JsonHelper.GetString(assistantContent, "role"), Is.EqualTo("model"));
        
        var parts = assistantContent?["parts"] as JsonArray;
        Assert.That(parts, Is.Not.Null);
        Assert.That(parts!.Count, Is.GreaterThanOrEqualTo(1));

        var functionCall = parts[0]?["functionCall"];
        Assert.That(functionCall, Is.Not.Null);
        Assert.That(JsonHelper.GetString(functionCall, "id"), Is.EqualTo("call_abc123"));
        Assert.That(JsonHelper.GetString(functionCall, "name"), Is.EqualTo("get_weather"));
        Assert.That(JsonHelper.GetString(functionCall, "args.location"), Is.EqualTo("San Francisco"));
    }

    /// <summary>
    /// Test that tool role messages are correctly formatted with ID matching.
    /// Validates Requirement 3.3: Tool role message formatting with ID matching.
    /// </summary>
    [Test]
    public void ToolRoleMessage_FormatsCorrectlyWithIdMatching()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "gpt-4o",
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "What's the weather?"
                },
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = (JsonNode?)null,
                    ["tool_calls"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["id"] = "call_xyz789",
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = "search_api",
                                ["arguments"] = "{\"query\":\"test\"}"
                            }
                        }
                    }
                },
                new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = "call_xyz789",
                    ["content"] = "{\"results\":[{\"title\":\"Result 1\"}],\"total\":1}"
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = OpenAIToAntigravityRequest.Convert("test-model", requestBytes, false);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var contents = result?["request"]?["contents"] as JsonArray;
        Assert.That(contents, Is.Not.Null);
        Assert.That(contents!.Count, Is.GreaterThanOrEqualTo(3));

        // Check tool response message
        var toolResponseContent = contents[2];
        Assert.That(JsonHelper.GetString(toolResponseContent, "role"), Is.EqualTo("user"));
        
        var toolParts = toolResponseContent?["parts"] as JsonArray;
        Assert.That(toolParts, Is.Not.Null);
        Assert.That(toolParts!.Count, Is.GreaterThanOrEqualTo(1));

        var functionResponse = toolParts[0]?["functionResponse"];
        Assert.That(functionResponse, Is.Not.Null);
        Assert.That(JsonHelper.GetString(functionResponse, "id"), Is.EqualTo("call_xyz789"));
        Assert.That(JsonHelper.GetString(functionResponse, "name"), Is.EqualTo("search_api"));
    }

    /// <summary>
    /// Test that function arguments are correctly parsed from string to JsonObject.
    /// Validates Requirement 3.4: Function arguments parsing (string → JsonObject).
    /// </summary>
    [Test]
    public void FunctionArguments_ParsedFromStringToJsonObject()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "gpt-4o",
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = (JsonNode?)null,
                    ["tool_calls"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["id"] = "call_test",
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = "complex_function",
                                ["arguments"] = "{\"nested\":{\"key\":\"value\"},\"array\":[1,2,3],\"number\":42}"
                            }
                        }
                    }
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = OpenAIToAntigravityRequest.Convert("test-model", requestBytes, false);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var contents = result?["request"]?["contents"] as JsonArray;
        Assert.That(contents, Is.Not.Null);
        
        var parts = contents![0]?["parts"] as JsonArray;
        Assert.That(parts, Is.Not.Null);

        var functionCall = parts![0]?["functionCall"];
        Assert.That(functionCall, Is.Not.Null);

        var args = functionCall?["args"];
        Assert.That(args, Is.Not.Null);
        Assert.That(JsonHelper.GetString(args, "nested.key"), Is.EqualTo("value"));
        Assert.That(JsonHelper.GetInt(args, "number"), Is.EqualTo(42));
    }

    #endregion

    #region 7.3.3 Test streaming with reasoning_content

    /// <summary>
    /// Test that streaming responses emit reasoning_content for thought content.
    /// Validates Requirement 3.4: reasoning_content in streaming deltas.
    /// </summary>
    [Test]
    public void StreamingResponse_ThoughtContent_EmitsReasoningContent()
    {
        // Arrange
        var state = new OpenAIStreamState();
        
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
                                    ["thought"] = true,
                                    ["text"] = "Let me think about this..."
                                }
                            }
                        }
                    }
                }
            }
        };

        var chunkBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(chunk));

        // Act
        var results = AntigravityToOpenAIResponse.ConvertStream("test-model", chunkBytes, state);

        // Assert
        Assert.That(results.Count, Is.EqualTo(1));
        var output = JsonNode.Parse(results[0]);
        Assert.That(output, Is.Not.Null);

        var delta = output?["choices"]?[0]?["delta"];
        Assert.That(delta, Is.Not.Null);
        Assert.That(JsonHelper.GetString(delta, "reasoning_content"), Is.EqualTo("Let me think about this..."));
        Assert.That(JsonHelper.GetString(delta, "role"), Is.EqualTo("assistant"));
    }

    /// <summary>
    /// Test that streaming responses emit regular content for non-thought text.
    /// </summary>
    [Test]
    public void StreamingResponse_RegularContent_EmitsContent()
    {
        // Arrange
        var state = new OpenAIStreamState();
        
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
                                    ["text"] = "Hello, how can I help you?"
                                }
                            }
                        }
                    }
                }
            }
        };

        var chunkBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(chunk));

        // Act
        var results = AntigravityToOpenAIResponse.ConvertStream("test-model", chunkBytes, state);

        // Assert
        Assert.That(results.Count, Is.EqualTo(1));
        var output = JsonNode.Parse(results[0]);
        Assert.That(output, Is.Not.Null);

        var delta = output?["choices"]?[0]?["delta"];
        Assert.That(delta, Is.Not.Null);
        Assert.That(JsonHelper.GetString(delta, "content"), Is.EqualTo("Hello, how can I help you?"));
        Assert.That(JsonHelper.GetString(delta, "role"), Is.EqualTo("assistant"));
    }

    /// <summary>
    /// Test that streaming tool_calls have unique IDs.
    /// Validates Requirement 3.5: tool_calls array formatting with unique IDs.
    /// </summary>
    [Test]
    public void StreamingResponse_ToolCalls_HaveUniqueIds()
    {
        // Arrange
        var state1 = new OpenAIStreamState();
        var state2 = new OpenAIStreamState();
        
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
        var results1 = AntigravityToOpenAIResponse.ConvertStream("test-model", chunkBytes, state1);
        var results2 = AntigravityToOpenAIResponse.ConvertStream("test-model", chunkBytes, state2);

        // Assert
        Assert.That(results1.Count, Is.EqualTo(1));
        Assert.That(results2.Count, Is.EqualTo(1));

        var output1 = JsonNode.Parse(results1[0]);
        var output2 = JsonNode.Parse(results2[0]);

        var toolCalls1 = output1?["choices"]?[0]?["delta"]?["tool_calls"] as JsonArray;
        var toolCalls2 = output2?["choices"]?[0]?["delta"]?["tool_calls"] as JsonArray;

        Assert.That(toolCalls1, Is.Not.Null);
        Assert.That(toolCalls2, Is.Not.Null);

        var id1 = JsonHelper.GetString(toolCalls1![0], "id");
        var id2 = JsonHelper.GetString(toolCalls2![0], "id");

        Assert.That(id1, Is.Not.Null);
        Assert.That(id2, Is.Not.Null);
        Assert.That(id1, Is.Not.EqualTo(id2), "Tool call IDs should be unique across calls");
        Assert.That(id1, Does.Contain("call_get_weather_"));
    }

    #endregion

    #region 7.3.4 Test image response handling

    /// <summary>
    /// Test that streaming responses handle inline image data correctly.
    /// Validates Requirement 3.6: Image URL handling with base64 data URI.
    /// </summary>
    [Test]
    public void StreamingResponse_InlineImage_FormatsAsDataUri()
    {
        // Arrange
        var state = new OpenAIStreamState();
        var base64Data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        
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
                                    ["inlineData"] = new JsonObject
                                    {
                                        ["mimeType"] = "image/png",
                                        ["data"] = base64Data
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
        var results = AntigravityToOpenAIResponse.ConvertStream("test-model", chunkBytes, state);

        // Assert
        Assert.That(results.Count, Is.EqualTo(1));
        var output = JsonNode.Parse(results[0]);
        Assert.That(output, Is.Not.Null);

        var delta = output?["choices"]?[0]?["delta"];
        Assert.That(delta, Is.Not.Null);

        var images = delta?["images"] as JsonArray;
        Assert.That(images, Is.Not.Null);
        Assert.That(images!.Count, Is.EqualTo(1));

        var imageUrl = JsonHelper.GetString(images[0], "image_url.url");
        Assert.That(imageUrl, Is.Not.Null);
        Assert.That(imageUrl, Does.StartWith("data:image/png;base64,"));
        Assert.That(imageUrl, Does.Contain(base64Data));
    }

    /// <summary>
    /// Test that non-streaming responses handle inline image data correctly.
    /// Validates Requirement 3.6: Image URL handling with base64 data URI.
    /// </summary>
    [Test]
    public void NonStreamingResponse_InlineImage_FormatsAsDataUri()
    {
        // Arrange
        var base64Data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        
        var response = new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["responseId"] = "resp_123",
                ["modelVersion"] = "test-model-v1",
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
                                    ["text"] = "Here is the image:"
                                },
                                new JsonObject
                                {
                                    ["inlineData"] = new JsonObject
                                    {
                                        ["mimeType"] = "image/jpeg",
                                        ["data"] = base64Data
                                    }
                                }
                            }
                        },
                        ["finishReason"] = "STOP"
                    }
                },
                ["usageMetadata"] = new JsonObject
                {
                    ["promptTokenCount"] = 10,
                    ["candidatesTokenCount"] = 20,
                    ["totalTokenCount"] = 30
                }
            }
        };

        var responseBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(response));

        // Act
        var result = AntigravityToOpenAIResponse.ConvertNonStream("test-model", responseBytes);
        var output = JsonNode.Parse(result);

        // Assert
        Assert.That(output, Is.Not.Null);

        var message = output?["choices"]?[0]?["message"];
        Assert.That(message, Is.Not.Null);
        Assert.That(JsonHelper.GetString(message, "content"), Is.EqualTo("Here is the image:"));

        var images = message?["images"] as JsonArray;
        Assert.That(images, Is.Not.Null);
        Assert.That(images!.Count, Is.EqualTo(1));

        var imageUrl = JsonHelper.GetString(images[0], "image_url.url");
        Assert.That(imageUrl, Is.Not.Null);
        Assert.That(imageUrl, Does.StartWith("data:image/jpeg;base64,"));
        Assert.That(imageUrl, Does.Contain(base64Data));
    }

    /// <summary>
    /// Test that usage statistics are correctly calculated in non-streaming response.
    /// Validates Requirement 3.7: Fix usage statistics in response.
    /// </summary>
    [Test]
    public void NonStreamingResponse_UsageStatistics_CalculatedCorrectly()
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
                                new JsonObject { ["text"] = "Hello" }
                            }
                        },
                        ["finishReason"] = "STOP"
                    }
                },
                ["usageMetadata"] = new JsonObject
                {
                    ["promptTokenCount"] = 100,
                    ["candidatesTokenCount"] = 50,
                    ["thoughtsTokenCount"] = 25,
                    ["totalTokenCount"] = 175,
                    ["cachedContentTokenCount"] = 20
                }
            }
        };

        var responseBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(response));

        // Act
        var result = AntigravityToOpenAIResponse.ConvertNonStream("test-model", responseBytes);
        var output = JsonNode.Parse(result);

        // Assert
        Assert.That(output, Is.Not.Null);

        var usage = output?["usage"];
        Assert.That(usage, Is.Not.Null);

        // prompt_tokens should be promptTokenCount - cachedTokenCount = 100 - 20 = 80
        Assert.That(JsonHelper.GetLong(usage, "prompt_tokens"), Is.EqualTo(80));
        
        // completion_tokens should be candidatesTokenCount + thoughtsTokenCount = 50 + 25 = 75
        Assert.That(JsonHelper.GetLong(usage, "completion_tokens"), Is.EqualTo(75));
        
        // total_tokens should be as reported
        Assert.That(JsonHelper.GetLong(usage, "total_tokens"), Is.EqualTo(175));

        // Check cached tokens details
        var promptDetails = usage?["prompt_tokens_details"];
        Assert.That(promptDetails, Is.Not.Null);
        Assert.That(JsonHelper.GetLong(promptDetails, "cached_tokens"), Is.EqualTo(20));

        // Check reasoning tokens details
        var completionDetails = usage?["completion_tokens_details"];
        Assert.That(completionDetails, Is.Not.Null);
        Assert.That(JsonHelper.GetLong(completionDetails, "reasoning_tokens"), Is.EqualTo(25));
    }

    /// <summary>
    /// Test that non-streaming response includes reasoning_content for thought content.
    /// </summary>
    [Test]
    public void NonStreamingResponse_ThoughtContent_IncludesReasoningContent()
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
                                    ["thought"] = true,
                                    ["text"] = "Let me think about this carefully..."
                                },
                                new JsonObject
                                {
                                    ["text"] = "The answer is 42."
                                }
                            }
                        },
                        ["finishReason"] = "STOP"
                    }
                },
                ["usageMetadata"] = new JsonObject
                {
                    ["promptTokenCount"] = 10,
                    ["candidatesTokenCount"] = 20,
                    ["totalTokenCount"] = 30
                }
            }
        };

        var responseBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(response));

        // Act
        var result = AntigravityToOpenAIResponse.ConvertNonStream("test-model", responseBytes);
        var output = JsonNode.Parse(result);

        // Assert
        Assert.That(output, Is.Not.Null);

        var message = output?["choices"]?[0]?["message"];
        Assert.That(message, Is.Not.Null);
        Assert.That(JsonHelper.GetString(message, "content"), Is.EqualTo("The answer is 42."));
        Assert.That(JsonHelper.GetString(message, "reasoning_content"), Is.EqualTo("Let me think about this carefully..."));
    }

    #endregion

    #region Task 11.6: OpenAI Translator Critical Fixes Tests

    #region 11.6.1 Test deep clean undefined

    /// <summary>
    /// Test that [undefined] strings are removed from request.
    /// Task 11.6.1: Test deep clean undefined.
    /// </summary>
    [Test]
    public void DeepCleanUndefined_RemovesUndefinedStrings()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "gpt-4o",
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "Hello",
                    ["name"] = "[undefined]"
                }
            },
            ["extra"] = "[undefined]"
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = OpenAIToAntigravityRequest.Convert("test-model", requestBytes, false);
        var resultStr = Encoding.UTF8.GetString(resultBytes);

        // Assert
        Assert.That(resultStr, Does.Not.Contain("[undefined]"));
    }

    #endregion

    #region 11.6.2 Test consecutive message merging

    /// <summary>
    /// Test that consecutive messages with same role are merged.
    /// Task 11.6.2: Test consecutive message merging.
    /// </summary>
    [Test]
    public void MergeConsecutiveContents_MergesSameRoleMessages()
    {
        // Arrange
        var contents = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = "Hello" } }
            },
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = "World" } }
            },
            new JsonObject
            {
                ["role"] = "model",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = "Hi there" } }
            }
        };

        // Act
        var merged = OpenAIToAntigravityRequest.MergeConsecutiveContents(contents);

        // Assert
        Assert.That(merged, Is.True);
        Assert.That(contents.Count, Is.EqualTo(2));
        
        var firstParts = contents[0]?["parts"] as JsonArray;
        Assert.That(firstParts?.Count, Is.EqualTo(2));
    }

    /// <summary>
    /// Test that alternating roles are not merged.
    /// </summary>
    [Test]
    public void MergeConsecutiveContents_DoesNotMergeAlternatingRoles()
    {
        // Arrange
        var contents = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = "Hello" } }
            },
            new JsonObject
            {
                ["role"] = "model",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = "Hi" } }
            },
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = "How are you?" } }
            }
        };

        // Act
        var merged = OpenAIToAntigravityRequest.MergeConsecutiveContents(contents);

        // Assert
        Assert.That(merged, Is.False);
        Assert.That(contents.Count, Is.EqualTo(3));
    }

    #endregion

    #region 11.6.3 Test thinking model detection

    /// <summary>
    /// Test that Gemini 3 thinking models are detected.
    /// Task 11.6.3: Test thinking model detection.
    /// </summary>
    [Test]
    [TestCase("gemini-3-pro", true)]
    [TestCase("gemini-3-flash-high", true)]
    [TestCase("gemini-3-ultra-low", true)]
    [TestCase("claude-3.5-sonnet-thinking", true)]
    [TestCase("gpt-4o", false)]
    [TestCase("gemini-2.0-flash", false)]
    [TestCase("claude-3.5-sonnet", false)]
    public void IsThinkingModel_DetectsCorrectly(string modelName, bool expected)
    {
        // Act
        var result = OpenAIToAntigravityRequest.IsThinkingModel(modelName);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    #endregion

    #region 11.6.4 Test placeholder thinking injection

    /// <summary>
    /// Test that placeholder thinking is injected for thinking models.
    /// Task 11.6.4: Test placeholder thinking injection.
    /// </summary>
    [Test]
    public void ThinkingModel_InjectsPlaceholderThinking()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "gemini-3-pro",
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = "Hello"
                },
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = "Hi there",
                    ["reasoning_content"] = "Let me think..."
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = OpenAIToAntigravityRequest.Convert("gemini-3-pro", requestBytes, false);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var contents = result?["request"]?["contents"] as JsonArray;
        Assert.That(contents, Is.Not.Null);
        
        // Find model message
        JsonNode? modelMsg = null;
        foreach (var c in contents!)
        {
            if (JsonHelper.GetString(c, "role") == "model")
            {
                modelMsg = c;
                break;
            }
        }
        
        Assert.That(modelMsg, Is.Not.Null);
        var parts = modelMsg?["parts"] as JsonArray;
        Assert.That(parts, Is.Not.Null);
        
        // Should have thought block
        var hasThought = false;
        foreach (var part in parts!)
        {
            if (JsonHelper.GetBool(part, "thought") == true)
            {
                hasThought = true;
                break;
            }
        }
        Assert.That(hasThought, Is.True);
    }

    /// <summary>
    /// Test that incompatible history disables thinking.
    /// </summary>
    [Test]
    public void HasIncompatibleAssistantHistory_DetectsCorrectly()
    {
        // Arrange - assistant without reasoning_content
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = "Hello"
            },
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = "Hi there"
                // No reasoning_content
            }
        };

        // Act
        var result = OpenAIToAntigravityRequest.HasIncompatibleAssistantHistory(messages);

        // Assert
        Assert.That(result, Is.True);
    }

    /// <summary>
    /// Test that compatible history allows thinking.
    /// </summary>
    [Test]
    public void HasIncompatibleAssistantHistory_CompatibleHistory()
    {
        // Arrange - assistant with reasoning_content
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = "Hello"
            },
            new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = "Hi there",
                ["reasoning_content"] = "Let me think..."
            }
        };

        // Act
        var result = OpenAIToAntigravityRequest.HasIncompatibleAssistantHistory(messages);

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region 11.6.5 Test tool schema cleaning and type enforcement

    /// <summary>
    /// Test that JSON schema is cleaned of invalid fields.
    /// Task 11.6.5: Test tool schema cleaning.
    /// </summary>
    [Test]
    public void CleanJsonSchema_RemovesInvalidFields()
    {
        // Arrange
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["format"] = "invalid",
            ["strict"] = true,
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "email"
                }
            }
        };

        // Act
        OpenAIToAntigravityRequest.CleanJsonSchema(schema);

        // Assert
        Assert.That(schema.ContainsKey("format"), Is.False);
        Assert.That(schema.ContainsKey("strict"), Is.False);
        Assert.That(schema.ContainsKey("additionalProperties"), Is.False);
        Assert.That(schema.ContainsKey("type"), Is.True);
        Assert.That(schema.ContainsKey("properties"), Is.True);
        
        var nameSchema = schema["properties"]?["name"] as JsonObject;
        Assert.That(nameSchema?.ContainsKey("format"), Is.False);
    }

    /// <summary>
    /// Test that types are converted to uppercase for Gemini.
    /// Task 11.6.5: Test type enforcement.
    /// </summary>
    [Test]
    public void EnforceUppercaseTypes_ConvertsToUppercase()
    {
        // Arrange
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string" },
                ["age"] = new JsonObject { ["type"] = "integer" },
                ["tags"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" }
                }
            }
        };

        // Act
        OpenAIToAntigravityRequest.EnforceUppercaseTypes(schema);

        // Assert
        Assert.That(JsonHelper.GetString(schema, "type"), Is.EqualTo("OBJECT"));
        Assert.That(JsonHelper.GetString(schema, "properties.name.type"), Is.EqualTo("STRING"));
        Assert.That(JsonHelper.GetString(schema, "properties.age.type"), Is.EqualTo("INTEGER"));
        Assert.That(JsonHelper.GetString(schema, "properties.tags.type"), Is.EqualTo("ARRAY"));
        Assert.That(JsonHelper.GetString(schema, "properties.tags.items.type"), Is.EqualTo("STRING"));
    }

    /// <summary>
    /// Test that local_shell_call is remapped to shell.
    /// Task 11.6.5: Test tool name remapping.
    /// </summary>
    [Test]
    public void ToolProcessing_RemapsLocalShellCallToShell()
    {
        // Arrange
        var request = new JsonObject
        {
            ["model"] = "gpt-4o",
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["tool_calls"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["id"] = "call_123",
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = "local_shell_call",
                                ["arguments"] = "{\"command\":\"ls\"}"
                            }
                        }
                    }
                }
            }
        };

        var requestBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(request));

        // Act
        var resultBytes = OpenAIToAntigravityRequest.Convert("test-model", requestBytes, false);
        var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

        // Assert
        var contents = result?["request"]?["contents"] as JsonArray;
        var parts = contents?[0]?["parts"] as JsonArray;
        var fcName = JsonHelper.GetString(parts?[0], "functionCall.name");
        
        Assert.That(fcName, Is.EqualTo("shell"));
    }

    #endregion

    #region 11.6.6 Test grounding metadata handling

    /// <summary>
    /// Test that grounding metadata is formatted as citations.
    /// Task 11.6.6: Test grounding metadata handling.
    /// </summary>
    [Test]
    public void FormatGroundingMetadata_FormatsCorrectly()
    {
        // Arrange
        var grounding = new JsonObject
        {
            ["webSearchQueries"] = new JsonArray { "test query 1", "test query 2" },
            ["groundingChunks"] = new JsonArray
            {
                new JsonObject
                {
                    ["web"] = new JsonObject
                    {
                        ["title"] = "Source 1",
                        ["uri"] = "https://example.com/1"
                    }
                },
                new JsonObject
                {
                    ["web"] = new JsonObject
                    {
                        ["title"] = "Source 2",
                        ["uri"] = "https://example.com/2"
                    }
                }
            }
        };

        // Act
        var result = AntigravityToOpenAIResponse.FormatGroundingMetadata(grounding);

        // Assert
        Assert.That(result, Does.Contain("test query 1"));
        Assert.That(result, Does.Contain("test query 2"));
        Assert.That(result, Does.Contain("[Source 1](https://example.com/1)"));
        Assert.That(result, Does.Contain("[Source 2](https://example.com/2)"));
    }

    /// <summary>
    /// Test that grounding metadata is included in response content.
    /// </summary>
    [Test]
    public void NonStreamingResponse_IncludesGroundingMetadata()
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
                                new JsonObject { ["text"] = "Here is the answer." }
                            }
                        },
                        ["finishReason"] = "STOP",
                        ["groundingMetadata"] = new JsonObject
                        {
                            ["webSearchQueries"] = new JsonArray { "search query" },
                            ["groundingChunks"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["web"] = new JsonObject
                                    {
                                        ["title"] = "Wikipedia",
                                        ["uri"] = "https://wikipedia.org"
                                    }
                                }
                            }
                        }
                    }
                },
                ["usageMetadata"] = new JsonObject
                {
                    ["promptTokenCount"] = 10,
                    ["candidatesTokenCount"] = 20,
                    ["totalTokenCount"] = 30
                }
            }
        };

        var responseBytes = Encoding.UTF8.GetBytes(JsonHelper.Stringify(response));

        // Act
        var result = AntigravityToOpenAIResponse.ConvertNonStream("test-model", responseBytes);
        var output = JsonNode.Parse(result);

        // Assert
        var content = JsonHelper.GetString(output, "choices.0.message.content");
        Assert.That(content, Does.Contain("Here is the answer."));
        Assert.That(content, Does.Contain("search query"));
        Assert.That(content, Does.Contain("Wikipedia"));
    }

    #endregion

    #region 11.6.7 Test function call args remapping

    /// <summary>
    /// Test that function call args are remapped correctly.
    /// Task 11.6.7: Test function call args remapping.
    /// </summary>
    [Test]
    [TestCase("grep", "description", "pattern")]
    [TestCase("grep", "query", "pattern")]
    [TestCase("glob", "description", "pattern")]
    [TestCase("search", "query", "pattern")]
    public void RemapFunctionCallArgs_RemapsCorrectly(string toolName, string fromKey, string toKey)
    {
        // Arrange
        var args = new JsonObject
        {
            [fromKey] = "test value"
        };

        // Act
        AntigravityToOpenAIResponse.RemapFunctionCallArgs(toolName, args);

        // Assert
        Assert.That(args.ContainsKey(fromKey), Is.False);
        Assert.That(args.ContainsKey(toKey), Is.True);
        Assert.That(JsonHelper.GetString(args, toKey), Is.EqualTo("test value"));
    }

    /// <summary>
    /// Test that paths array is remapped to path string.
    /// </summary>
    [Test]
    public void RemapFunctionCallArgs_RemapsPathsToPath()
    {
        // Arrange
        var args = new JsonObject
        {
            ["pattern"] = "*.ts",
            ["paths"] = new JsonArray { "/src", "/lib" }
        };

        // Act
        AntigravityToOpenAIResponse.RemapFunctionCallArgs("grep", args);

        // Assert
        Assert.That(args.ContainsKey("paths"), Is.False);
        Assert.That(args.ContainsKey("path"), Is.True);
        Assert.That(JsonHelper.GetString(args, "path"), Is.EqualTo("/src"));
    }

    /// <summary>
    /// Test that EnterPlanMode clears all args.
    /// </summary>
    [Test]
    public void RemapFunctionCallArgs_EnterPlanMode_ClearsAllArgs()
    {
        // Arrange
        var args = new JsonObject
        {
            ["arg1"] = "value1",
            ["arg2"] = "value2"
        };

        // Act
        AntigravityToOpenAIResponse.RemapFunctionCallArgs("EnterPlanMode", args);

        // Assert
        Assert.That(args.Count, Is.EqualTo(0));
    }

    /// <summary>
    /// Test Base64 signature decoding.
    /// Task 11.5.3
    /// </summary>
    [Test]
    public void DecodeBase64Signature_DecodesValidBase64()
    {
        // Arrange
        var originalSig = "This is a valid signature text for testing purposes";
        var base64Sig = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalSig));

        // Act
        var result = AntigravityToOpenAIResponse.DecodeBase64Signature(base64Sig);

        // Assert
        Assert.That(result, Is.EqualTo(originalSig));
    }

    /// <summary>
    /// Test that non-Base64 signatures are returned as-is.
    /// </summary>
    [Test]
    public void DecodeBase64Signature_ReturnsOriginalIfNotBase64()
    {
        // Arrange
        var signature = "not-valid-base64!!!";

        // Act
        var result = AntigravityToOpenAIResponse.DecodeBase64Signature(signature);

        // Assert
        Assert.That(result, Is.EqualTo(signature));
    }

    #endregion

    #endregion
}
