using System.Text.Json.Nodes;
using AntiBridge.Core.Services;
using FsCheck;
using NUnit.Framework;
using PropertyAttribute = FsCheck.NUnit.PropertyAttribute;

namespace AntiBridge.Tests;

/// <summary>
/// Property-based tests for ContextManager.
/// Feature: antibridge-feature-port
/// </summary>
[TestFixture]
public class ContextManagerPropertyTests
{
    #region Custom Generators

    /// <summary>
    /// Generator for ASCII-only strings (characters 0-127).
    /// </summary>
    private static Arbitrary<string> AsciiStringArbitrary()
    {
        var asciiChars = Enumerable.Range(32, 95).Select(i => (char)i).ToArray();
        
        return Gen.Elements(asciiChars)
            .ArrayOf()
            .Where(arr => arr.Length <= 1000)
            .Select(arr => new string(arr))
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for Unicode-only strings (CJK characters).
    /// </summary>
    private static Arbitrary<string> UnicodeStringArbitrary()
    {
        var cjkChars = Enumerable.Range(0x4E00, 500).Select(i => (char)i).ToArray();
        
        return Gen.Elements(cjkChars)
            .ArrayOf()
            .Where(arr => arr.Length <= 500)
            .Select(arr => new string(arr))
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for mixed ASCII and Unicode strings.
    /// </summary>
    private static Arbitrary<string> MixedStringArbitrary()
    {
        var asciiChars = Enumerable.Range(32, 95).Select(i => (char)i).ToArray();
        var cjkChars = Enumerable.Range(0x4E00, 200).Select(i => (char)i).ToArray();
        var allChars = asciiChars.Concat(cjkChars).ToArray();
        
        return Gen.Elements(allChars)
            .ArrayOf()
            .Where(arr => arr.Length <= 500)
            .Select(arr => new string(arr))
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for any valid string (empty, ASCII, Unicode, or mixed).
    /// </summary>
    private static Arbitrary<string> AnyStringArbitrary()
    {
        var emptyGen = Gen.Constant(string.Empty);
        var asciiGen = AsciiStringArbitrary().Generator;
        var unicodeGen = UnicodeStringArbitrary().Generator;
        var mixedGen = MixedStringArbitrary().Generator;
        
        return Gen.OneOf(emptyGen, asciiGen, unicodeGen, mixedGen).ToArbitrary();
    }

    #endregion

    #region Property 9.2.1: Token estimation consistency

    /// <summary>
    /// Property: Token estimation consistency - same input text MUST produce same token count.
    /// **Validates: Requirements 9.2.1**
    /// </summary>
    [Property(MaxTest = 200, StartSize = 0, EndSize = 100)]
    public Property TokenEstimation_SameInputAlwaysProducesSameOutput()
    {
        var stringGen = AnyStringArbitrary().Generator;

        return Prop.ForAll(
            stringGen.ToArbitrary(),
            text =>
            {
                var result1 = ContextManager.EstimateTokens(text);
                var result2 = ContextManager.EstimateTokens(text);
                var result3 = ContextManager.EstimateTokens(text);
                var result4 = ContextManager.EstimateTokens(text);
                var result5 = ContextManager.EstimateTokens(text);

                var allEqual = result1 == result2 && result2 == result3 && result3 == result4 && result4 == result5;
                var isEmpty = string.IsNullOrEmpty(text);
                var isAsciiOnly = !isEmpty && text.All(c => c < 128);
                var isUnicodeOnly = !isEmpty && text.All(c => c >= 128);
                var isMixed = !isEmpty && !isAsciiOnly && !isUnicodeOnly;

                return allEqual
                    .Label($"EstimateTokens returned different results: [{result1}, {result2}, {result3}, {result4}, {result5}]")
                    .Classify(isEmpty, "empty string")
                    .Classify(isAsciiOnly, "ASCII only")
                    .Classify(isUnicodeOnly, "Unicode only")
                    .Classify(isMixed, "mixed content");
            });
    }

    /// <summary>
    /// Property: Token estimation consistency for ASCII-only strings.
    /// **Validates: Requirements 9.2.1**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 0, EndSize = 100)]
    public Property TokenEstimation_AsciiOnly_Consistent()
    {
        var asciiGen = AsciiStringArbitrary().Generator;

        return Prop.ForAll(
            asciiGen.ToArbitrary(),
            text =>
            {
                var result1 = ContextManager.EstimateTokens(text);
                var result2 = ContextManager.EstimateTokens(text);
                var result3 = ContextManager.EstimateTokens(text);
                var allEqual = result1 == result2 && result2 == result3;

                return allEqual
                    .Label($"ASCII EstimateTokens inconsistent: [{result1}, {result2}, {result3}]")
                    .Classify(text.Length == 0, "empty")
                    .Classify(text.Length > 0 && text.Length <= 4, "1 token worth")
                    .Classify(text.Length > 4 && text.Length <= 40, "few tokens")
                    .Classify(text.Length > 40, "many tokens");
            });
    }

    /// <summary>
    /// Property: Token estimation consistency for Unicode-only strings.
    /// **Validates: Requirements 9.2.1**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 0, EndSize = 100)]
    public Property TokenEstimation_UnicodeOnly_Consistent()
    {
        var unicodeGen = UnicodeStringArbitrary().Generator;

        return Prop.ForAll(
            unicodeGen.ToArbitrary(),
            text =>
            {
                var result1 = ContextManager.EstimateTokens(text);
                var result2 = ContextManager.EstimateTokens(text);
                var result3 = ContextManager.EstimateTokens(text);
                var allEqual = result1 == result2 && result2 == result3;

                return allEqual
                    .Label($"Unicode EstimateTokens inconsistent: [{result1}, {result2}, {result3}]")
                    .Classify(text.Length == 0, "empty")
                    .Classify(text.Length > 0 && text.Length <= 2, "1-2 chars")
                    .Classify(text.Length > 2 && text.Length <= 15, "few chars")
                    .Classify(text.Length > 15, "many chars");
            });
    }

    /// <summary>
    /// Property: Token estimation consistency for mixed content.
    /// **Validates: Requirements 9.2.1**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 0, EndSize = 100)]
    public Property TokenEstimation_MixedContent_Consistent()
    {
        var mixedGen = MixedStringArbitrary().Generator;

        return Prop.ForAll(
            mixedGen.ToArbitrary(),
            text =>
            {
                var result1 = ContextManager.EstimateTokens(text);
                var result2 = ContextManager.EstimateTokens(text);
                var result3 = ContextManager.EstimateTokens(text);
                var allEqual = result1 == result2 && result2 == result3;
                var asciiCount = text.Count(c => c < 128);
                var asciiRatio = text.Length > 0 ? (double)asciiCount / text.Length : 0;

                return allEqual
                    .Label($"Mixed EstimateTokens inconsistent: [{result1}, {result2}, {result3}]")
                    .Classify(text.Length == 0, "empty")
                    .Classify(asciiRatio > 0.7, "mostly ASCII")
                    .Classify(asciiRatio >= 0.3 && asciiRatio <= 0.7, "balanced mix")
                    .Classify(asciiRatio < 0.3 && text.Length > 0, "mostly Unicode");
            });
    }

    /// <summary>
    /// Property: Empty and null strings always return 0 tokens.
    /// **Validates: Requirements 9.2.1**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property TokenEstimation_EmptyAndNull_AlwaysZero()
    {
        return Prop.ForAll(
            Gen.Constant(0).ToArbitrary(),
            _ =>
            {
                var emptyResult1 = ContextManager.EstimateTokens("");
                var emptyResult2 = ContextManager.EstimateTokens("");
                var nullResult1 = ContextManager.EstimateTokens(null);
                var nullResult2 = ContextManager.EstimateTokens(null);

                var emptyConsistent = emptyResult1 == 0 && emptyResult2 == 0;
                var nullConsistent = nullResult1 == 0 && nullResult2 == 0;

                return (emptyConsistent && nullConsistent)
                    .Label($"Empty/null inconsistent: empty=[{emptyResult1},{emptyResult2}], null=[{nullResult1},{nullResult2}]");
            });
    }

    /// <summary>
    /// Property: Interleaved calls still consistent.
    /// **Validates: Requirements 9.2.1**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 50)]
    public Property TokenEstimation_InterleavedCalls_StillConsistent()
    {
        var stringGen = AnyStringArbitrary().Generator;

        return Prop.ForAll(
            stringGen.ToArbitrary(),
            stringGen.ToArbitrary(),
            (text1, text2) =>
            {
                var result1_first = ContextManager.EstimateTokens(text1);
                var result2_first = ContextManager.EstimateTokens(text2);
                var result1_second = ContextManager.EstimateTokens(text1);
                var result2_second = ContextManager.EstimateTokens(text2);

                var text1Consistent = result1_first == result1_second;
                var text2Consistent = result2_first == result2_second;

                return (text1Consistent && text2Consistent)
                    .Label($"Interleaved calls inconsistent")
                    .Classify(text1 == text2, "same inputs")
                    .Classify(text1 != text2, "different inputs");
            });
    }

    /// <summary>
    /// Property: Token estimation is deterministic (no randomness).
    /// **Validates: Requirements 9.2.1**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 100)]
    public Property TokenEstimation_IsDeterministic_NoRandomness()
    {
        var stringGen = AnyStringArbitrary().Generator;

        return Prop.ForAll(
            stringGen.ToArbitrary(),
            text =>
            {
                var results = new int[20];
                for (int i = 0; i < 20; i++)
                {
                    results[i] = ContextManager.EstimateTokens(text);
                }

                var firstResult = results[0];
                var allSame = results.All(r => r == firstResult);

                return allSame
                    .Label($"Detected randomness: results varied from {results.Min()} to {results.Max()}")
                    .Classify(text?.Length == 0, "empty")
                    .Classify(text?.Length > 0 && text?.Length < 50, "short-medium")
                    .Classify(text?.Length >= 50, "long");
            });
    }

    #endregion

    #region Property 9.2.2: Compression monotonicity - tokens never increase

    /// <summary>
    /// Generator for tool_use content blocks.
    /// </summary>
    private static Gen<JsonObject> ToolUseBlockGen()
    {
        return from toolId in Gen.Elements("tool_1", "tool_2", "tool_3", "tool_4", "tool_5")
               from toolName in Gen.Elements("read_file", "write_file", "search", "execute", "list_dir")
               from inputText in AsciiStringArbitrary().Generator
               select new JsonObject
               {
                   ["type"] = "tool_use",
                   ["id"] = toolId,
                   ["name"] = toolName,
                   ["input"] = new JsonObject { ["content"] = inputText }
               };
    }

    /// <summary>
    /// Generator for tool_result content blocks.
    /// </summary>
    private static Gen<JsonObject> ToolResultBlockGen()
    {
        return from toolId in Gen.Elements("tool_1", "tool_2", "tool_3", "tool_4", "tool_5")
               from resultText in AsciiStringArbitrary().Generator
               select new JsonObject
               {
                   ["type"] = "tool_result",
                   ["tool_use_id"] = toolId,
                   ["content"] = resultText
               };
    }

    /// <summary>
    /// Generator for thinking content blocks with valid signatures.
    /// </summary>
    private static Gen<JsonObject> ThinkingBlockGen()
    {
        var signatureGen = Gen.Elements(Enumerable.Range('A', 26).Select(i => (char)i).ToArray())
            .ArrayOf(60)
            .Select(arr => new string(arr));

        return from thinkingText in AsciiStringArbitrary().Generator
               from signature in signatureGen
               select new JsonObject
               {
                   ["type"] = "thinking",
                   ["thinking"] = thinkingText,
                   ["signature"] = signature
               };
    }

    /// <summary>
    /// Generator for text content blocks.
    /// </summary>
    private static Gen<JsonObject> TextBlockGen()
    {
        return from text in AsciiStringArbitrary().Generator
               select new JsonObject
               {
                   ["type"] = "text",
                   ["text"] = text
               };
    }

    /// <summary>
    /// Generator for assistant messages with tool_use blocks.
    /// </summary>
    private static Gen<JsonObject> AssistantToolUseMessageGen()
    {
        return from toolUseBlock in ToolUseBlockGen()
               select new JsonObject
               {
                   ["role"] = "assistant",
                   ["content"] = new JsonArray(toolUseBlock)
               };
    }

    /// <summary>
    /// Generator for user messages with tool_result blocks.
    /// </summary>
    private static Gen<JsonObject> UserToolResultMessageGen()
    {
        return from toolResultBlock in ToolResultBlockGen()
               select new JsonObject
               {
                   ["role"] = "user",
                   ["content"] = new JsonArray(toolResultBlock)
               };
    }

    /// <summary>
    /// Generator for assistant messages with thinking blocks.
    /// </summary>
    private static Gen<JsonObject> AssistantThinkingMessageGen()
    {
        return from thinkingBlock in ThinkingBlockGen()
               from textBlock in TextBlockGen()
               select new JsonObject
               {
                   ["role"] = "assistant",
                   ["content"] = new JsonArray(thinkingBlock, textBlock)
               };
    }

    /// <summary>
    /// Generator for a tool round (assistant tool_use + user tool_result).
    /// </summary>
    private static Gen<List<JsonObject>> ToolRoundGen()
    {
        return from assistantMsg in AssistantToolUseMessageGen()
               from userMsg in UserToolResultMessageGen()
               select new List<JsonObject> { assistantMsg, userMsg };
    }

    /// <summary>
    /// Generator for message arrays with multiple tool rounds.
    /// </summary>
    private static Gen<JsonArray> MessagesWithToolRoundsGen(int count)
    {
        return Gen.ListOf(count, ToolRoundGen())
            .Select(rounds => new JsonArray(rounds.SelectMany(r => r).Cast<JsonNode>().ToArray()));
    }

    /// <summary>
    /// Generator for message arrays with thinking blocks.
    /// </summary>
    private static Gen<JsonArray> MessagesWithThinkingGen(int count)
    {
        return Gen.ListOf(count, AssistantThinkingMessageGen())
            .Select(messages => new JsonArray(messages.Cast<JsonNode>().ToArray()));
    }

    /// <summary>
    /// Generator for a full request with messages.
    /// </summary>
    private static Gen<JsonObject> RequestWithMessagesGen(Gen<JsonArray> messagesGen)
    {
        return from messages in messagesGen
               from systemText in AsciiStringArbitrary().Generator
               select new JsonObject
               {
                   ["system"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = systemText }),
                   ["messages"] = messages
               };
    }

    /// <summary>
    /// Property: TrimToolMessages monotonicity - after trimming, token count MUST be ≤ before.
    /// **Validates: Requirements 9.2.2**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 6, EndSize = 15)]
    public Property TrimToolMessages_TokensNeverIncrease()
    {
        return Prop.ForAll(
            Gen.Choose(6, 15).ToArbitrary(),
            roundCount =>
            {
                // Generate messages with tool rounds
                var messages = MessagesWithToolRoundsGen(roundCount).Sample(1, 1).First();
                var messagesCopy = JsonNode.Parse(messages.ToJsonString())!.AsArray();

                // Estimate tokens before trimming
                var tokensBefore = EstimateMessagesTokens(messagesCopy);

                // Apply trimming
                var wasModified = ContextManager.TrimToolMessages(messagesCopy, keepLastNRounds: 5);

                // Estimate tokens after trimming
                var tokensAfter = EstimateMessagesTokens(messagesCopy);

                // Property: tokens after MUST be <= tokens before
                var monotonic = tokensAfter <= tokensBefore;

                var roundsBefore = ContextManager.IdentifyToolRounds(messages).Count;
                var roundsAfter = ContextManager.IdentifyToolRounds(messagesCopy).Count;

                return monotonic
                    .Label($"Token count increased: before={tokensBefore}, after={tokensAfter}, rounds: {roundsBefore}->{roundsAfter}")
                    .Classify(wasModified, "trimming applied")
                    .Classify(!wasModified, "no trimming needed")
                    .Classify(roundsBefore > 10, "many rounds (>10)")
                    .Classify(roundsBefore <= 10 && roundsBefore > 5, "moderate rounds (6-10)");
            });
    }

    /// <summary>
    /// Property: CompressThinkingPreserveSignature monotonicity - after compression, token count MUST be ≤ before.
    /// **Validates: Requirements 9.2.2**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 5, EndSize = 15)]
    public Property CompressThinkingPreserveSignature_TokensNeverIncrease()
    {
        return Prop.ForAll(
            Gen.Choose(5, 15).ToArbitrary(),
            msgCount =>
            {
                // Generate messages with thinking blocks
                var messages = MessagesWithThinkingGen(msgCount).Sample(1, 1).First();
                var messagesCopy = JsonNode.Parse(messages.ToJsonString())!.AsArray();

                // Estimate tokens before compression
                var tokensBefore = EstimateMessagesTokens(messagesCopy);

                // Apply compression
                var wasModified = ContextManager.CompressThinkingPreserveSignature(messagesCopy, protectedLastN: 4);

                // Estimate tokens after compression
                var tokensAfter = EstimateMessagesTokens(messagesCopy);

                // Property: tokens after MUST be <= tokens before
                var monotonic = tokensAfter <= tokensBefore;

                var thinkingBlocksBefore = CountThinkingBlocks(messages);
                var thinkingBlocksAfter = CountThinkingBlocks(messagesCopy);

                return monotonic
                    .Label($"Token count increased: before={tokensBefore}, after={tokensAfter}, thinking: {thinkingBlocksBefore}->{thinkingBlocksAfter}")
                    .Classify(wasModified, "compression applied")
                    .Classify(!wasModified, "no compression needed")
                    .Classify(messages.Count > 10, "many messages (>10)")
                    .Classify(messages.Count <= 10 && messages.Count > 4, "moderate messages (5-10)");
            });
    }

    /// <summary>
    /// Property: ApplyProgressiveCompression monotonicity - after any compression layer, token count MUST be ≤ before.
    /// **Validates: Requirements 9.2.2**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 10)]
    public Property ApplyProgressiveCompression_TokensNeverIncrease()
    {
        return Prop.ForAll(
            Gen.Choose(3, 8).ToArbitrary(),  // tool rounds
            Gen.Choose(2, 6).ToArbitrary(),  // thinking messages
            Gen.Choose(1000, 5000).ToArbitrary(), // maxTokens
            (toolRoundCount, thinkingCount, maxTokens) =>
            {
                // Generate mixed messages
                var toolRounds = MessagesWithToolRoundsGen(toolRoundCount).Sample(1, 1).First();
                var thinkingMsgs = MessagesWithThinkingGen(thinkingCount).Sample(1, 1).First();
                
                var allMessages = new JsonArray();
                foreach (var msg in toolRounds) allMessages.Add(JsonNode.Parse(msg!.ToJsonString()));
                foreach (var msg in thinkingMsgs) allMessages.Add(JsonNode.Parse(msg!.ToJsonString()));

                var request = new JsonObject
                {
                    ["system"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "System prompt" }),
                    ["messages"] = allMessages
                };

                var requestCopy = JsonNode.Parse(request.ToJsonString())!.AsObject();

                // Estimate tokens before compression
                var tokensBefore = ContextManager.EstimateRequestTokens(requestCopy);

                // Apply progressive compression
                var result = ContextManager.ApplyProgressiveCompression(requestCopy, maxTokens);

                // Estimate tokens after compression
                var tokensAfter = ContextManager.EstimateRequestTokens(requestCopy);

                // Property: tokens after MUST be <= tokens before
                var monotonic = tokensAfter <= tokensBefore;
                var pressureBefore = maxTokens > 0 ? tokensBefore * 100 / maxTokens : 0;

                return monotonic
                    .Label($"Token count increased: before={tokensBefore}, after={tokensAfter}, layers=[{string.Join(",", result.LayersApplied)}]")
                    .Classify(result.WasCompressed, "compression applied")
                    .Classify(!result.WasCompressed, "no compression needed")
                    .Classify(result.LayersApplied.Contains(1), "Layer 1 applied")
                    .Classify(result.LayersApplied.Contains(2), "Layer 2 applied")
                    .Classify(pressureBefore >= 60, "high pressure (>=60%)");
            });
    }

    /// <summary>
    /// Property: TrimToolMessages with varying keepLastNRounds - tokens never increase.
    /// **Validates: Requirements 9.2.2**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 15)]
    public Property TrimToolMessages_VaryingKeepRounds_TokensNeverIncrease()
    {
        return Prop.ForAll(
            Gen.Choose(1, 20).ToArbitrary(), // round count
            Gen.Choose(1, 10).ToArbitrary(), // keepLastNRounds
            (roundCount, keepLastNRounds) =>
            {
                var messages = MessagesWithToolRoundsGen(roundCount).Sample(1, 1).First();
                var messagesCopy = JsonNode.Parse(messages.ToJsonString())!.AsArray();

                var tokensBefore = EstimateMessagesTokens(messagesCopy);
                ContextManager.TrimToolMessages(messagesCopy, keepLastNRounds);
                var tokensAfter = EstimateMessagesTokens(messagesCopy);

                var monotonic = tokensAfter <= tokensBefore;

                return monotonic
                    .Label($"Token count increased with keepLastNRounds={keepLastNRounds}: before={tokensBefore}, after={tokensAfter}")
                    .Classify(keepLastNRounds <= 3, "aggressive trim (<=3)")
                    .Classify(keepLastNRounds > 3 && keepLastNRounds <= 7, "moderate trim (4-7)")
                    .Classify(keepLastNRounds > 7, "conservative trim (>7)");
            });
    }

    /// <summary>
    /// Property: CompressThinkingPreserveSignature with varying protectedLastN - tokens never increase.
    /// **Validates: Requirements 9.2.2**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 15)]
    public Property CompressThinkingPreserveSignature_VaryingProtection_TokensNeverIncrease()
    {
        return Prop.ForAll(
            Gen.Choose(1, 20).ToArbitrary(), // message count
            Gen.Choose(0, 10).ToArbitrary(), // protectedLastN
            (msgCount, protectedLastN) =>
            {
                var messages = MessagesWithThinkingGen(msgCount).Sample(1, 1).First();
                var messagesCopy = JsonNode.Parse(messages.ToJsonString())!.AsArray();

                var tokensBefore = EstimateMessagesTokens(messagesCopy);
                ContextManager.CompressThinkingPreserveSignature(messagesCopy, protectedLastN);
                var tokensAfter = EstimateMessagesTokens(messagesCopy);

                var monotonic = tokensAfter <= tokensBefore;

                return monotonic
                    .Label($"Token count increased with protectedLastN={protectedLastN}: before={tokensBefore}, after={tokensAfter}")
                    .Classify(protectedLastN == 0, "no protection")
                    .Classify(protectedLastN > 0 && protectedLastN <= 4, "standard protection (1-4)")
                    .Classify(protectedLastN > 4, "high protection (>4)");
            });
    }

    /// <summary>
    /// Property: ApplyProgressiveCompression with varying thresholds - tokens never increase.
    /// **Validates: Requirements 9.2.2**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 10)]
    public Property ApplyProgressiveCompression_VaryingThresholds_TokensNeverIncrease()
    {
        // Use a tuple generator to combine parameters
        var paramsGen = from toolRoundCount in Gen.Choose(2, 6)
                        from thinkingCount in Gen.Choose(2, 5)
                        from maxTokens in Gen.Choose(500, 3000)
                        from layer1 in Gen.Choose(30, 70)
                        select (toolRoundCount, thinkingCount, maxTokens, layer1);

        return Prop.ForAll(
            paramsGen.ToArbitrary(),
            p =>
            {
                // Generate mixed messages
                var toolRounds = MessagesWithToolRoundsGen(p.toolRoundCount).Sample(1, 1).First();
                var thinkingMsgs = MessagesWithThinkingGen(p.thinkingCount).Sample(1, 1).First();
                
                var allMessages = new JsonArray();
                foreach (var msg in toolRounds) allMessages.Add(JsonNode.Parse(msg!.ToJsonString()));
                foreach (var msg in thinkingMsgs) allMessages.Add(JsonNode.Parse(msg!.ToJsonString()));

                var request = new JsonObject
                {
                    ["system"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "System prompt" }),
                    ["messages"] = allMessages
                };

                var requestCopy = JsonNode.Parse(request.ToJsonString())!.AsObject();

                var tokensBefore = ContextManager.EstimateRequestTokens(requestCopy);
                
                // Use ascending thresholds
                var l2 = p.layer1 + 15;
                var l3 = l2 + 15;
                var result = ContextManager.ApplyProgressiveCompression(requestCopy, p.maxTokens, p.layer1, l2, l3);

                var tokensAfter = ContextManager.EstimateRequestTokens(requestCopy);
                var monotonic = tokensAfter <= tokensBefore;

                return monotonic
                    .Label($"Token count increased with thresholds=[{p.layer1},{l2},{l3}]: before={tokensBefore}, after={tokensAfter}")
                    .Classify(p.layer1 <= 40, "low layer1 threshold (<=40)")
                    .Classify(p.layer1 > 40, "high layer1 threshold (>40)")
                    .Classify(result.LayersApplied.Count == 0, "no layers applied")
                    .Classify(result.LayersApplied.Count >= 1, "layers applied");
            });
    }

    /// <summary>
    /// Helper method to estimate tokens for a messages array.
    /// </summary>
    private static int EstimateMessagesTokens(JsonArray messages)
    {
        // Clone messages to avoid "node already has parent" error
        var messagesClone = JsonNode.Parse(messages.ToJsonString())!.AsArray();
        var request = new JsonObject { ["messages"] = messagesClone };
        return ContextManager.EstimateRequestTokens(request);
    }

    /// <summary>
    /// Helper method to count thinking blocks in a messages array.
    /// </summary>
    private static int CountThinkingBlocks(JsonArray messages)
    {
        int count = 0;
        foreach (var msg in messages)
        {
            var content = msg?["content"]?.AsArray();
            if (content == null) continue;
            
            foreach (var block in content)
            {
                if (block?["type"]?.GetValue<string>() == "thinking")
                    count++;
            }
        }
        return count;
    }

    #endregion

    #region Property 9.2.3: Signature preservation - no signatures lost in Layer 2

    /// <summary>
    /// Generator for thinking blocks with valid signatures (>= 50 chars).
    /// </summary>
    private static Gen<JsonObject> ThinkingBlockWithValidSignatureGen()
    {
        var signatureGen = Gen.Elements(Enumerable.Range('A', 26).Select(i => (char)i).ToArray())
            .ArrayOf(60)
            .Select(arr => new string(arr));

        return from thinkingText in AsciiStringArbitrary().Generator
               from signature in signatureGen
               select new JsonObject
               {
                   ["type"] = "thinking",
                   ["thinking"] = thinkingText,
                   ["signature"] = signature
               };
    }

    /// <summary>
    /// Generator for assistant messages with thinking blocks that have valid signatures.
    /// </summary>
    private static Gen<JsonObject> AssistantThinkingWithSignatureMessageGen()
    {
        return from thinkingBlock in ThinkingBlockWithValidSignatureGen()
               from textBlock in TextBlockGen()
               select new JsonObject
               {
                   ["role"] = "assistant",
                   ["content"] = new JsonArray(thinkingBlock, textBlock)
               };
    }

    /// <summary>
    /// Generator for message arrays with thinking blocks that have valid signatures.
    /// </summary>
    private static Gen<JsonArray> MessagesWithValidSignaturesGen(int count)
    {
        return Gen.ListOf(count, AssistantThinkingWithSignatureMessageGen())
            .Select(messages => new JsonArray(messages.Cast<JsonNode>().ToArray()));
    }

    /// <summary>
    /// Helper method to extract all signatures from a messages array.
    /// </summary>
    private static List<string> ExtractSignatures(JsonArray messages)
    {
        var signatures = new List<string>();
        foreach (var msg in messages)
        {
            var content = msg?["content"]?.AsArray();
            if (content == null) continue;
            
            foreach (var block in content)
            {
                if (block?["type"]?.GetValue<string>() == "thinking")
                {
                    var sig = block?["signature"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(sig))
                        signatures.Add(sig);
                }
            }
        }
        return signatures;
    }

    /// <summary>
    /// Property: CompressThinkingPreserveSignature MUST preserve all valid signatures.
    /// **Validates: Requirements 9.2.3**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 5, EndSize = 15)]
    public Property CompressThinkingPreserveSignature_PreservesAllSignatures()
    {
        return Prop.ForAll(
            Gen.Choose(5, 15).ToArbitrary(),
            msgCount =>
            {
                // Generate messages with valid signatures
                var messages = MessagesWithValidSignaturesGen(msgCount).Sample(1, 1).First();
                var messagesCopy = JsonNode.Parse(messages.ToJsonString())!.AsArray();

                // Extract signatures before compression
                var signaturesBefore = ExtractSignatures(messages);

                // Apply compression (protect last 2 messages)
                ContextManager.CompressThinkingPreserveSignature(messagesCopy, protectedLastN: 2);

                // Extract signatures after compression
                var signaturesAfter = ExtractSignatures(messagesCopy);

                // Property: All signatures from before MUST still exist after
                var allPreserved = signaturesBefore.All(sig => signaturesAfter.Contains(sig));

                return allPreserved
                    .Label($"Signatures lost: before={signaturesBefore.Count}, after={signaturesAfter.Count}")
                    .Classify(signaturesBefore.Count == signaturesAfter.Count, "all signatures preserved")
                    .Classify(signaturesBefore.Count > signaturesAfter.Count, "signatures lost (BUG!)");
            });
    }

    /// <summary>
    /// Property: CompressThinkingPreserveSignature with varying protection - signatures always preserved.
    /// **Validates: Requirements 9.2.3**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 15)]
    public Property CompressThinkingPreserveSignature_VaryingProtection_SignaturesPreserved()
    {
        return Prop.ForAll(
            Gen.Choose(3, 15).ToArbitrary(), // message count
            Gen.Choose(0, 10).ToArbitrary(), // protectedLastN
            (msgCount, protectedLastN) =>
            {
                var messages = MessagesWithValidSignaturesGen(msgCount).Sample(1, 1).First();
                var messagesCopy = JsonNode.Parse(messages.ToJsonString())!.AsArray();

                var signaturesBefore = ExtractSignatures(messages);
                ContextManager.CompressThinkingPreserveSignature(messagesCopy, protectedLastN);
                var signaturesAfter = ExtractSignatures(messagesCopy);

                var allPreserved = signaturesBefore.All(sig => signaturesAfter.Contains(sig));

                return allPreserved
                    .Label($"Signatures lost with protectedLastN={protectedLastN}: before={signaturesBefore.Count}, after={signaturesAfter.Count}")
                    .Classify(protectedLastN == 0, "no protection")
                    .Classify(protectedLastN > 0 && protectedLastN <= 4, "standard protection")
                    .Classify(protectedLastN > 4, "high protection");
            });
    }

    /// <summary>
    /// Property: ExtractLastValidSignature returns a signature >= 50 chars or null.
    /// **Validates: Requirements 9.2.3**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 3, EndSize = 12)]
    public Property ExtractLastValidSignature_ReturnsValidOrNull()
    {
        return Prop.ForAll(
            Gen.Choose(3, 12).ToArbitrary(),
            msgCount =>
            {
                var messages = MessagesWithValidSignaturesGen(msgCount).Sample(1, 1).First();

                var signature = ContextManager.ExtractLastValidSignature(messages);

                // Property: signature is either null or >= 50 chars
                var isValid = signature == null || signature.Length >= 50;

                return isValid
                    .Label($"Invalid signature length: {signature?.Length ?? 0}")
                    .Classify(signature != null, "signature found")
                    .Classify(signature == null, "no signature found");
            });
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Truncates a string for display in test output.
    /// </summary>
    private static string TruncateForDisplay(string? text, int maxLength = 30)
    {
        if (string.IsNullOrEmpty(text))
            return "<empty>";
        
        if (text.Length <= maxLength)
            return text;
        
        return text.Substring(0, maxLength) + "...";
    }

    #endregion
}
