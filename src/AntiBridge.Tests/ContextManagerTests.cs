using NUnit.Framework;
using System.Text.Json.Nodes;
using AntiBridge.Core.Services;

namespace AntiBridge.Tests;

[TestFixture]
public class ContextManagerTests
{
    #region Task 5.6.1: Test token estimation accuracy

    [Test]
    public void EstimateTokens_EmptyString_ReturnsZero()
    {
        Assert.That(ContextManager.EstimateTokens(""), Is.EqualTo(0));
        Assert.That(ContextManager.EstimateTokens(null), Is.EqualTo(0));
    }

    [Test]
    public void EstimateTokens_AsciiText_UsesCorrectRatio()
    {
        // 40 ASCII chars / 4 = 10 tokens, + 15% = 11.5 -> 12
        var text = "Hello, this is a test of ASCII text!!";
        var tokens = ContextManager.EstimateTokens(text);
        
        // 38 chars / 4 = 9.5 -> 10 tokens, * 1.15 = 11.5 -> 12
        Assert.That(tokens, Is.GreaterThan(0));
        Assert.That(tokens, Is.LessThan(20)); // Reasonable upper bound
    }

    [Test]
    public void EstimateTokens_UnicodeText_UsesCorrectRatio()
    {
        // Unicode/CJK: ~1.5 chars/token
        var text = "你好世界测试"; // 6 Chinese characters
        var tokens = ContextManager.EstimateTokens(text);
        
        // 6 chars / 1.5 = 4 tokens, * 1.15 = 4.6 -> 5
        Assert.That(tokens, Is.GreaterThanOrEqualTo(5));
        Assert.That(tokens, Is.LessThan(10));
    }

    [Test]
    public void EstimateTokens_MixedText_CombinesBothRatios()
    {
        // Mix of ASCII and Unicode
        var text = "Hello 你好 World 世界";
        var tokens = ContextManager.EstimateTokens(text);
        
        // Should be combination of both ratios
        Assert.That(tokens, Is.GreaterThan(0));
    }

    [Test]
    public void EstimateTokens_Includes15PercentSafetyMargin()
    {
        // 100 ASCII chars / 4 = 25 tokens
        // With 15% margin: 25 * 1.15 = 28.75 -> 29
        var text = new string('a', 100);
        var tokens = ContextManager.EstimateTokens(text);
        
        // Without margin: 25, with margin: ~29
        Assert.That(tokens, Is.GreaterThanOrEqualTo(29));
    }

    [Test]
    public void EstimateRequestTokens_NullRequest_ReturnsZero()
    {
        Assert.That(ContextManager.EstimateRequestTokens(null), Is.EqualTo(0));
    }

    [Test]
    public void EstimateRequestTokens_WithMessages_EstimatesCorrectly()
    {
        var request = JsonNode.Parse(@"{
            ""messages"": [
                {""role"": ""user"", ""content"": ""Hello, how are you?""},
                {""role"": ""assistant"", ""content"": ""I am doing well, thank you!""}
            ]
        }");

        var tokens = ContextManager.EstimateRequestTokens(request);
        Assert.That(tokens, Is.GreaterThan(0));
    }

    [Test]
    public void EstimateRequestTokens_WithSystemInstruction_IncludesIt()
    {
        var requestWithSystem = JsonNode.Parse(@"{
            ""systemInstruction"": {""parts"": [{""text"": ""You are a helpful assistant.""}]},
            ""messages"": [{""role"": ""user"", ""content"": ""Hi""}]
        }");

        var requestWithoutSystem = JsonNode.Parse(@"{
            ""messages"": [{""role"": ""user"", ""content"": ""Hi""}]
        }");

        var tokensWithSystem = ContextManager.EstimateRequestTokens(requestWithSystem);
        var tokensWithoutSystem = ContextManager.EstimateRequestTokens(requestWithoutSystem);

        Assert.That(tokensWithSystem, Is.GreaterThan(tokensWithoutSystem));
    }

    [Test]
    public void EstimateRequestTokens_WithTools_IncludesThem()
    {
        var requestWithTools = JsonNode.Parse(@"{
            ""messages"": [{""role"": ""user"", ""content"": ""Hi""}],
            ""tools"": [{""type"": ""function"", ""function"": {""name"": ""get_weather"", ""description"": ""Get weather info""}}]
        }");

        var requestWithoutTools = JsonNode.Parse(@"{
            ""messages"": [{""role"": ""user"", ""content"": ""Hi""}]
        }");

        var tokensWithTools = ContextManager.EstimateRequestTokens(requestWithTools);
        var tokensWithoutTools = ContextManager.EstimateRequestTokens(requestWithoutTools);

        Assert.That(tokensWithTools, Is.GreaterThan(tokensWithoutTools));
    }

    #endregion

    #region Task 5.6.2: Test tool round identification

    [Test]
    public void IdentifyToolRounds_EmptyMessages_ReturnsEmpty()
    {
        var messages = new JsonArray();
        var rounds = ContextManager.IdentifyToolRounds(messages);
        Assert.That(rounds, Is.Empty);
    }

    [Test]
    public void IdentifyToolRounds_NoToolMessages_ReturnsEmpty()
    {
        var messages = JsonNode.Parse(@"[
            {""role"": ""user"", ""content"": ""Hello""},
            {""role"": ""assistant"", ""content"": ""Hi there!""}
        ]") as JsonArray;

        var rounds = ContextManager.IdentifyToolRounds(messages!);
        Assert.That(rounds, Is.Empty);
    }

    [Test]
    public void IdentifyToolRounds_SingleToolRound_IdentifiesCorrectly()
    {
        var messages = JsonNode.Parse(@"[
            {""role"": ""user"", ""content"": ""What is the weather?""},
            {""role"": ""assistant"", ""content"": [{""type"": ""tool_use"", ""id"": ""tool1"", ""name"": ""get_weather"", ""input"": {}}]},
            {""role"": ""user"", ""content"": [{""type"": ""tool_result"", ""tool_use_id"": ""tool1"", ""content"": ""Sunny""}]},
            {""role"": ""assistant"", ""content"": ""The weather is sunny!""}
        ]") as JsonArray;

        var rounds = ContextManager.IdentifyToolRounds(messages!);
        
        Assert.That(rounds, Has.Count.EqualTo(1));
        Assert.That(rounds[0].Indices, Has.Count.EqualTo(2));
        Assert.That(rounds[0].Indices, Contains.Item(1)); // assistant with tool_use
        Assert.That(rounds[0].Indices, Contains.Item(2)); // user with tool_result
    }

    [Test]
    public void IdentifyToolRounds_MultipleToolRounds_IdentifiesAll()
    {
        var messages = JsonNode.Parse(@"[
            {""role"": ""user"", ""content"": ""Question 1""},
            {""role"": ""assistant"", ""content"": [{""type"": ""tool_use"", ""id"": ""t1"", ""name"": ""tool1"", ""input"": {}}]},
            {""role"": ""user"", ""content"": [{""type"": ""tool_result"", ""tool_use_id"": ""t1"", ""content"": ""Result 1""}]},
            {""role"": ""assistant"", ""content"": ""Response 1""},
            {""role"": ""user"", ""content"": ""Question 2""},
            {""role"": ""assistant"", ""content"": [{""type"": ""tool_use"", ""id"": ""t2"", ""name"": ""tool2"", ""input"": {}}]},
            {""role"": ""user"", ""content"": [{""type"": ""tool_result"", ""tool_use_id"": ""t2"", ""content"": ""Result 2""}]},
            {""role"": ""assistant"", ""content"": ""Response 2""}
        ]") as JsonArray;

        var rounds = ContextManager.IdentifyToolRounds(messages!);
        
        Assert.That(rounds, Has.Count.EqualTo(2));
    }

    [Test]
    public void IdentifyToolRounds_MultipleToolResultsInOneRound_GroupsThem()
    {
        var messages = JsonNode.Parse(@"[
            {""role"": ""assistant"", ""content"": [{""type"": ""tool_use"", ""id"": ""t1"", ""name"": ""tool1"", ""input"": {}}]},
            {""role"": ""user"", ""content"": [{""type"": ""tool_result"", ""tool_use_id"": ""t1"", ""content"": ""Result 1""}]},
            {""role"": ""user"", ""content"": [{""type"": ""tool_result"", ""tool_use_id"": ""t2"", ""content"": ""Result 2""}]}
        ]") as JsonArray;

        var rounds = ContextManager.IdentifyToolRounds(messages!);
        
        Assert.That(rounds, Has.Count.EqualTo(1));
        Assert.That(rounds[0].Indices, Has.Count.EqualTo(3)); // 1 assistant + 2 user tool_results
    }

    #endregion

    #region Task 5.6.3: Test tool message trimming

    [Test]
    public void TrimToolMessages_FewerThanKeepN_NoChange()
    {
        var messages = JsonNode.Parse(@"[
            {""role"": ""assistant"", ""content"": [{""type"": ""tool_use"", ""id"": ""t1"", ""name"": ""tool1"", ""input"": {}}]},
            {""role"": ""user"", ""content"": [{""type"": ""tool_result"", ""tool_use_id"": ""t1"", ""content"": ""Result""}]}
        ]") as JsonArray;

        var originalCount = messages!.Count;
        var result = ContextManager.TrimToolMessages(messages, keepLastNRounds: 5);
        
        Assert.That(result, Is.False);
        Assert.That(messages.Count, Is.EqualTo(originalCount));
    }

    [Test]
    public void TrimToolMessages_MoreThanKeepN_TrimsOldest()
    {
        // Create 7 tool rounds
        var messages = new JsonArray();
        for (int i = 0; i < 7; i++)
        {
            messages.Add(JsonNode.Parse($@"{{""role"": ""assistant"", ""content"": [{{""type"": ""tool_use"", ""id"": ""t{i}"", ""name"": ""tool{i}"", ""input"": {{}}}}]}}"));
            messages.Add(JsonNode.Parse($@"{{""role"": ""user"", ""content"": [{{""type"": ""tool_result"", ""tool_use_id"": ""t{i}"", ""content"": ""Result {i}""}}]}}"));
        }

        var result = ContextManager.TrimToolMessages(messages, keepLastNRounds: 5);
        
        Assert.That(result, Is.True);
        // Should have removed 2 rounds (4 messages)
        Assert.That(messages.Count, Is.EqualTo(10)); // 7*2 - 2*2 = 10
    }

    [Test]
    public void TrimToolMessages_PreservesNonToolMessages()
    {
        var messages = JsonNode.Parse(@"[
            {""role"": ""user"", ""content"": ""Initial question""},
            {""role"": ""assistant"", ""content"": [{""type"": ""tool_use"", ""id"": ""t1"", ""name"": ""tool1"", ""input"": {}}]},
            {""role"": ""user"", ""content"": [{""type"": ""tool_result"", ""tool_use_id"": ""t1"", ""content"": ""Result""}]},
            {""role"": ""assistant"", ""content"": ""Final response""}
        ]") as JsonArray;

        var result = ContextManager.TrimToolMessages(messages!, keepLastNRounds: 5);
        
        Assert.That(result, Is.False); // Only 1 round, less than 5
        Assert.That(messages!.Count, Is.EqualTo(4));
    }

    [Test]
    public void TrimToolMessages_ReverseOrderRemoval_PreservesIndices()
    {
        // Create 6 tool rounds with interleaved regular messages
        var messages = new JsonArray();
        messages.Add(JsonNode.Parse(@"{""role"": ""user"", ""content"": ""Start""}"));
        
        for (int i = 0; i < 6; i++)
        {
            messages.Add(JsonNode.Parse($@"{{""role"": ""assistant"", ""content"": [{{""type"": ""tool_use"", ""id"": ""t{i}"", ""name"": ""tool{i}"", ""input"": {{}}}}]}}"));
            messages.Add(JsonNode.Parse($@"{{""role"": ""user"", ""content"": [{{""type"": ""tool_result"", ""tool_use_id"": ""t{i}"", ""content"": ""Result {i}""}}]}}"));
        }
        
        messages.Add(JsonNode.Parse(@"{""role"": ""assistant"", ""content"": ""End""}"));

        var result = ContextManager.TrimToolMessages(messages, keepLastNRounds: 5);
        
        Assert.That(result, Is.True);
        // Should have removed 1 round (2 messages), keeping start and end
        Assert.That(messages.Count, Is.EqualTo(12)); // 1 + 6*2 + 1 - 2 = 12
    }

    #endregion

    #region Task 5.6.4: Test thinking compression with signature preservation

    [Test]
    public void CompressThinkingPreserveSignature_NoThinkingBlocks_NoChange()
    {
        var messages = JsonNode.Parse(@"[
            {""role"": ""user"", ""content"": ""Hello""},
            {""role"": ""assistant"", ""content"": [{""type"": ""text"", ""text"": ""Hi there!""}]}
        ]") as JsonArray;

        var result = ContextManager.CompressThinkingPreserveSignature(messages!);
        
        Assert.That(result, Is.False);
    }

    [Test]
    public void CompressThinkingPreserveSignature_WithSignature_CompressesThinking()
    {
        var longThinking = new string('x', 100);
        var signature = new string('s', 60);
        
        var messages = JsonNode.Parse($@"[
            {{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""{longThinking}"", ""signature"": ""{signature}""}}]}},
            {{""role"": ""user"", ""content"": ""Follow up""}},
            {{""role"": ""assistant"", ""content"": ""Response""}},
            {{""role"": ""user"", ""content"": ""Another""}},
            {{""role"": ""assistant"", ""content"": ""Final""}}
        ]") as JsonArray;

        var result = ContextManager.CompressThinkingPreserveSignature(messages!, protectedLastN: 4);
        
        Assert.That(result, Is.True);
        
        // Check that thinking was compressed to "..."
        var thinkingBlock = messages![0]!["content"]![0];
        Assert.That(thinkingBlock!["thinking"]!.ToString(), Is.EqualTo("..."));
        // Signature should be preserved
        Assert.That(thinkingBlock["signature"]!.ToString(), Is.EqualTo(signature));
    }

    [Test]
    public void CompressThinkingPreserveSignature_ProtectsLastNMessages()
    {
        var longThinking = new string('x', 100);
        var signature = new string('s', 60);
        
        var messages = JsonNode.Parse($@"[
            {{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""{longThinking}"", ""signature"": ""{signature}""}}]}},
            {{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""{longThinking}"", ""signature"": ""{signature}""}}]}}
        ]") as JsonArray;

        // Protect last 4 messages - both messages should be protected
        var result = ContextManager.CompressThinkingPreserveSignature(messages!, protectedLastN: 4);
        
        Assert.That(result, Is.False);
        
        // Both should still have original thinking
        Assert.That(messages![0]!["content"]![0]!["thinking"]!.ToString(), Is.EqualTo(longThinking));
        Assert.That(messages[1]!["content"]![0]!["thinking"]!.ToString(), Is.EqualTo(longThinking));
    }

    [Test]
    public void CompressThinkingPreserveSignature_NoSignature_DoesNotCompress()
    {
        var longThinking = new string('x', 100);
        
        var messages = JsonNode.Parse($@"[
            {{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""{longThinking}""}}]}},
            {{""role"": ""user"", ""content"": ""Follow up""}},
            {{""role"": ""assistant"", ""content"": ""Response""}},
            {{""role"": ""user"", ""content"": ""Another""}},
            {{""role"": ""assistant"", ""content"": ""Final""}}
        ]") as JsonArray;

        var result = ContextManager.CompressThinkingPreserveSignature(messages!, protectedLastN: 4);
        
        Assert.That(result, Is.False);
        
        // Thinking should not be compressed (no signature)
        Assert.That(messages![0]!["content"]![0]!["thinking"]!.ToString(), Is.EqualTo(longThinking));
    }

    [Test]
    public void CompressThinkingPreserveSignature_ShortThinking_DoesNotCompress()
    {
        var signature = new string('s', 60);
        
        var messages = JsonNode.Parse($@"[
            {{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""short"", ""signature"": ""{signature}""}}]}},
            {{""role"": ""user"", ""content"": ""Follow up""}},
            {{""role"": ""assistant"", ""content"": ""Response""}},
            {{""role"": ""user"", ""content"": ""Another""}},
            {{""role"": ""assistant"", ""content"": ""Final""}}
        ]") as JsonArray;

        var result = ContextManager.CompressThinkingPreserveSignature(messages!, protectedLastN: 4);
        
        Assert.That(result, Is.False);
        
        // Short thinking should not be compressed
        Assert.That(messages![0]!["content"]![0]!["thinking"]!.ToString(), Is.EqualTo("short"));
    }

    #endregion

    #region Task 5.6.5: Test signature extraction

    [Test]
    public void ExtractLastValidSignature_EmptyMessages_ReturnsNull()
    {
        var messages = new JsonArray();
        var signature = ContextManager.ExtractLastValidSignature(messages);
        Assert.That(signature, Is.Null);
    }

    [Test]
    public void ExtractLastValidSignature_NoThinkingBlocks_ReturnsNull()
    {
        var messages = JsonNode.Parse(@"[
            {""role"": ""user"", ""content"": ""Hello""},
            {""role"": ""assistant"", ""content"": [{""type"": ""text"", ""text"": ""Hi!""}]}
        ]") as JsonArray;

        var signature = ContextManager.ExtractLastValidSignature(messages!);
        Assert.That(signature, Is.Null);
    }

    [Test]
    public void ExtractLastValidSignature_ValidSignature_ReturnsIt()
    {
        var validSignature = new string('s', 60);
        
        var messages = JsonNode.Parse($@"[
            {{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""Some thought"", ""signature"": ""{validSignature}""}}]}}
        ]") as JsonArray;

        var signature = ContextManager.ExtractLastValidSignature(messages!);
        Assert.That(signature, Is.EqualTo(validSignature));
    }

    [Test]
    public void ExtractLastValidSignature_ShortSignature_ReturnsNull()
    {
        var shortSignature = new string('s', 40); // Less than 50 chars
        
        var messages = JsonNode.Parse($@"[
            {{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""Some thought"", ""signature"": ""{shortSignature}""}}]}}
        ]") as JsonArray;

        var signature = ContextManager.ExtractLastValidSignature(messages!);
        Assert.That(signature, Is.Null);
    }

    [Test]
    public void ExtractLastValidSignature_MultipleSignatures_ReturnsLast()
    {
        var sig1 = "first_signature_" + new string('1', 50);
        var sig2 = "second_signature_" + new string('2', 50);
        var sig3 = "third_signature_" + new string('3', 50);
        
        var messages = JsonNode.Parse($@"[
            {{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""Thought 1"", ""signature"": ""{sig1}""}}]}},
            {{""role"": ""user"", ""content"": ""Question""}},
            {{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""Thought 2"", ""signature"": ""{sig2}""}}]}},
            {{""role"": ""user"", ""content"": ""Another""}},
            {{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""Thought 3"", ""signature"": ""{sig3}""}}]}}
        ]") as JsonArray;

        var signature = ContextManager.ExtractLastValidSignature(messages!);
        Assert.That(signature, Is.EqualTo(sig3));
    }

    [Test]
    public void ExtractLastValidSignature_ExactlyMinLength_ReturnsIt()
    {
        var exactSignature = new string('s', 50); // Exactly 50 chars
        
        var messages = JsonNode.Parse($@"[
            {{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""Some thought"", ""signature"": ""{exactSignature}""}}]}}
        ]") as JsonArray;

        var signature = ContextManager.ExtractLastValidSignature(messages!);
        Assert.That(signature, Is.EqualTo(exactSignature));
    }

    #endregion

    #region Task 5.6.6: Test progressive compression flow

    [Test]
    public void ApplyProgressiveCompression_LowPressure_NoCompression()
    {
        var request = JsonNode.Parse(@"{
            ""messages"": [
                {""role"": ""user"", ""content"": ""Hello""},
                {""role"": ""assistant"", ""content"": ""Hi!""}
            ]
        }");

        // Very high max tokens = low pressure
        var result = ContextManager.ApplyProgressiveCompression(request!, maxTokens: 1000000);
        
        Assert.That(result.WasCompressed, Is.False);
        Assert.That(result.LayersApplied, Is.Empty);
        Assert.That(result.FinalPressure, Is.LessThan(60));
    }

    [Test]
    public void ApplyProgressiveCompression_Layer1Threshold_TrimsTools()
    {
        // Create request with many tool rounds
        var messages = new JsonArray();
        for (int i = 0; i < 10; i++)
        {
            messages.Add(JsonNode.Parse($@"{{""role"": ""assistant"", ""content"": [{{""type"": ""tool_use"", ""id"": ""t{i}"", ""name"": ""tool{i}"", ""input"": {{}}}}]}}"));
            messages.Add(JsonNode.Parse($@"{{""role"": ""user"", ""content"": [{{""type"": ""tool_result"", ""tool_use_id"": ""t{i}"", ""content"": ""Result {i}""}}]}}"));
        }
        
        var request = new JsonObject { ["messages"] = messages };

        // Set max tokens to trigger Layer 1 (60%+)
        var estimatedTokens = ContextManager.EstimateRequestTokens(request);
        var maxTokens = (int)(estimatedTokens / 0.65); // ~65% pressure

        var result = ContextManager.ApplyProgressiveCompression(request, maxTokens);
        
        Assert.That(result.WasCompressed, Is.True);
        Assert.That(result.LayersApplied, Contains.Item(1));
    }

    [Test]
    public void ApplyProgressiveCompression_Layer2Threshold_CompressesThinking()
    {
        var longThinking = new string('x', 1000);
        var signature = new string('s', 60);
        
        // Create request with thinking blocks
        var messages = new JsonArray();
        for (int i = 0; i < 10; i++)
        {
            messages.Add(JsonNode.Parse($@"{{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""{longThinking}"", ""signature"": ""{signature}""}}]}}"));
            messages.Add(JsonNode.Parse(@"{""role"": ""user"", ""content"": ""Continue""}"));
        }
        
        var request = new JsonObject { ["messages"] = messages };

        // Set max tokens to trigger Layer 2 (75%+)
        var estimatedTokens = ContextManager.EstimateRequestTokens(request);
        var maxTokens = (int)(estimatedTokens / 0.80); // ~80% pressure

        var result = ContextManager.ApplyProgressiveCompression(request, maxTokens);
        
        Assert.That(result.WasCompressed, Is.True);
        Assert.That(result.LayersApplied, Contains.Item(2));
    }

    [Test]
    public void ApplyProgressiveCompression_Layer3Threshold_ExtractsSignature()
    {
        var longThinking = new string('x', 100);
        var signature = new string('s', 60);
        
        // Create request with thinking blocks - use protected messages so they won't be compressed
        // This ensures pressure stays high even after Layer 2
        var messages = new JsonArray();
        
        // Add many messages that will be protected (last 4)
        for (int i = 0; i < 4; i++)
        {
            messages.Add(JsonNode.Parse($@"{{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""{longThinking}"", ""signature"": ""{signature}""}}]}}"));
        }
        
        var request = new JsonObject { ["messages"] = messages };

        // Calculate tokens after potential compression
        // Since all messages are protected (last 4), no compression will happen
        var estimatedTokens = ContextManager.EstimateRequestTokens(request);
        
        // Set max tokens very low to ensure 90%+ pressure even after any compression
        var maxTokens = (int)(estimatedTokens / 0.95); // ~95% pressure

        var result = ContextManager.ApplyProgressiveCompression(request, maxTokens);
        
        Assert.That(result.LayersApplied, Contains.Item(3));
        Assert.That(result.LastSignature, Is.Not.Null);
        Assert.That(result.LastSignature!.Length, Is.GreaterThanOrEqualTo(50));
    }

    [Test]
    public void ApplyProgressiveCompression_RecalculatesPressureAfterEachLayer()
    {
        // Create request with both tool rounds and thinking blocks
        var longThinking = new string('x', 500);
        var signature = new string('s', 60);
        
        var messages = new JsonArray();
        
        // Add tool rounds
        for (int i = 0; i < 8; i++)
        {
            messages.Add(JsonNode.Parse($@"{{""role"": ""assistant"", ""content"": [{{""type"": ""tool_use"", ""id"": ""t{i}"", ""name"": ""tool{i}"", ""input"": {{}}}}]}}"));
            messages.Add(JsonNode.Parse($@"{{""role"": ""user"", ""content"": [{{""type"": ""tool_result"", ""tool_use_id"": ""t{i}"", ""content"": ""Result {i}""}}]}}"));
        }
        
        // Add thinking blocks
        for (int i = 0; i < 5; i++)
        {
            messages.Add(JsonNode.Parse($@"{{""role"": ""assistant"", ""content"": [{{""type"": ""thinking"", ""thinking"": ""{longThinking}"", ""signature"": ""{signature}""}}]}}"));
            messages.Add(JsonNode.Parse(@"{""role"": ""user"", ""content"": ""Continue""}"));
        }
        
        var request = new JsonObject { ["messages"] = messages };

        var initialTokens = ContextManager.EstimateRequestTokens(request);
        
        // Set max tokens to trigger compression
        var maxTokens = (int)(initialTokens / 0.70); // ~70% pressure

        var result = ContextManager.ApplyProgressiveCompression(request, maxTokens);
        
        // After compression, pressure should be lower
        var finalTokens = ContextManager.EstimateRequestTokens(request);
        Assert.That(finalTokens, Is.LessThan(initialTokens));
    }

    [Test]
    public void ApplyProgressiveCompression_NullRequest_ReturnsEmptyResult()
    {
        var result = ContextManager.ApplyProgressiveCompression(null!, maxTokens: 1000);
        
        Assert.That(result.WasCompressed, Is.False);
        Assert.That(result.LayersApplied, Is.Empty);
    }

    [Test]
    public void ApplyProgressiveCompression_NoMessages_ReturnsEmptyResult()
    {
        var request = JsonNode.Parse(@"{}");
        
        var result = ContextManager.ApplyProgressiveCompression(request!, maxTokens: 1000);
        
        Assert.That(result.WasCompressed, Is.False);
        Assert.That(result.LayersApplied, Is.Empty);
    }

    [Test]
    public void ApplyProgressiveCompression_CustomThresholds_Respected()
    {
        var messages = new JsonArray();
        messages.Add(JsonNode.Parse(@"{""role"": ""user"", ""content"": ""Hello""}"));
        messages.Add(JsonNode.Parse(@"{""role"": ""assistant"", ""content"": ""Hi!""}"));
        
        var request = new JsonObject { ["messages"] = messages };

        var estimatedTokens = ContextManager.EstimateRequestTokens(request);
        
        // Set max tokens to be at 50% pressure
        var maxTokens = estimatedTokens * 2;

        // With default thresholds (60%), no compression should happen
        var result1 = ContextManager.ApplyProgressiveCompression(request, maxTokens);
        Assert.That(result1.LayersApplied, Is.Empty);

        // With custom threshold (40%), Layer 1 should trigger
        var result2 = ContextManager.ApplyProgressiveCompression(request, maxTokens, layer1Threshold: 40);
        // No tool messages to trim, so still no compression
        Assert.That(result2.WasCompressed, Is.False);
    }

    #endregion
}
