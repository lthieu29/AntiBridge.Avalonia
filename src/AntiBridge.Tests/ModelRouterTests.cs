using NUnit.Framework;
using AntiBridge.Core.Services;

namespace AntiBridge.Tests;

[TestFixture]
public class ModelRouterTests
{
    private ModelRouter _router = null!;

    [SetUp]
    public void SetUp()
    {
        _router = new ModelRouter();
    }

    #region Task 2.3.1: Test exact match priority over wildcard

    [Test]
    public void ResolveModel_ExactMatchPriority_OverWildcard()
    {
        // Setup: Add both exact and wildcard mappings
        _router.SetCustomMapping("claude-3-opus", "exact-target");
        _router.SetCustomMapping("claude-*", "wildcard-target");

        // Act
        var result = _router.ResolveModel("claude-3-opus");

        // Assert: Exact match should win
        Assert.That(result, Is.EqualTo("exact-target"));
    }

    [Test]
    public void ResolveModel_WildcardUsed_WhenNoExactMatch()
    {
        // Setup: Add only wildcard mapping
        _router.SetCustomMapping("claude-*", "wildcard-target");

        // Act
        var result = _router.ResolveModel("claude-3-opus");

        // Assert: Wildcard should match
        Assert.That(result, Is.EqualTo("wildcard-target"));
    }

    [Test]
    public void ResolveModel_ExactMatchPriority_OverMoreSpecificWildcard()
    {
        // Setup: Add exact match and a very specific wildcard
        _router.SetCustomMapping("claude-3-opus", "exact-target");
        _router.SetCustomMapping("claude-3-*", "specific-wildcard-target");

        // Act
        var result = _router.ResolveModel("claude-3-opus");

        // Assert: Exact match should still win
        Assert.That(result, Is.EqualTo("exact-target"));
    }

    #endregion

    #region Task 2.3.2: Test wildcard specificity selection

    [Test]
    public void ResolveModel_WildcardSpecificity_MostSpecificWins()
    {
        // Setup: Add wildcards with different specificity
        _router.SetCustomMapping("*", "least-specific");           // specificity: 0
        _router.SetCustomMapping("claude-*", "medium-specific");   // specificity: 7
        _router.SetCustomMapping("claude-3-*", "most-specific");   // specificity: 9

        // Act
        var result = _router.ResolveModel("claude-3-opus");

        // Assert: Most specific pattern should win
        Assert.That(result, Is.EqualTo("most-specific"));
    }

    [Test]
    public void ResolveModel_WildcardSpecificity_LongerPatternWins()
    {
        // Setup: Two wildcards, one more specific
        _router.SetCustomMapping("gpt-*", "short-pattern");        // specificity: 4
        _router.SetCustomMapping("gpt-4-*", "longer-pattern");     // specificity: 6

        // Act
        var result = _router.ResolveModel("gpt-4-turbo");

        // Assert: Longer pattern should win
        Assert.That(result, Is.EqualTo("longer-pattern"));
    }

    [Test]
    public void ResolveModel_WildcardSpecificity_CountsNonWildcardChars()
    {
        // Setup: Patterns with different non-wildcard character counts
        _router.SetCustomMapping("a*c", "short-pattern");   // specificity: 2 (a, c)
        _router.SetCustomMapping("ab*c", "longer-pattern"); // specificity: 3 (ab, c)

        // Act - test with a string that matches both
        var result = _router.ResolveModel("ab-test-c");

        // Assert: Pattern with more non-wildcard chars should win
        Assert.That(result, Is.EqualTo("longer-pattern"));
    }

    #endregion

    #region Task 2.3.3: Test multiple wildcards in pattern

    [Test]
    public void ResolveModel_MultipleWildcards_MatchesCorrectly()
    {
        // Setup: Pattern with multiple wildcards
        _router.SetCustomMapping("claude-*-sonnet-*", "sonnet-target");

        // Act
        var result = _router.ResolveModel("claude-3-5-sonnet-20241022");

        // Assert
        Assert.That(result, Is.EqualTo("sonnet-target"));
    }

    [Test]
    public void ResolveModel_MultipleWildcards_MiddlePattern()
    {
        // Setup: Pattern with wildcards at start and end
        _router.SetCustomMapping("*-opus-*", "opus-target");

        // Act
        var result = _router.ResolveModel("claude-opus-4");

        // Assert
        Assert.That(result, Is.EqualTo("opus-target"));
    }

    [Test]
    public void ResolveModel_MultipleWildcards_ThreeWildcards()
    {
        // Setup: Pattern with three wildcards
        _router.SetCustomMapping("*claude*sonnet*", "complex-target");

        // Act
        var result = _router.ResolveModel("my-claude-3-sonnet-model");

        // Assert
        Assert.That(result, Is.EqualTo("complex-target"));
    }

    [Test]
    public void ResolveModel_MultipleWildcards_NoMatch()
    {
        // Setup: Pattern with multiple wildcards
        _router.SetCustomMapping("claude-*-opus-*", "opus-target");

        // Act - model doesn't contain "opus"
        var result = _router.ResolveModel("claude-3-sonnet-20241022");

        // Assert: Should fall back to default
        Assert.That(result, Is.EqualTo("claude-sonnet-4-5"));
    }

    [Test]
    public void WildcardMatch_ConsecutiveWildcards_Matches()
    {
        // Test consecutive wildcards
        Assert.That(ModelRouter.WildcardMatch("a**b", "a-x-b"), Is.True);
        Assert.That(ModelRouter.WildcardMatch("a**b", "ab"), Is.True);
    }

    #endregion

    #region Task 2.3.4: Test case-sensitive matching

    [Test]
    public void ResolveModel_CaseSensitive_ExactMatch()
    {
        // Setup
        _router.SetCustomMapping("Claude-3-Opus", "uppercase-target");

        // Act & Assert: Different case should not match
        var result1 = _router.ResolveModel("claude-3-opus");
        Assert.That(result1, Is.Not.EqualTo("uppercase-target"));

        // Act & Assert: Same case should match
        var result2 = _router.ResolveModel("Claude-3-Opus");
        Assert.That(result2, Is.EqualTo("uppercase-target"));
    }

    [Test]
    public void ResolveModel_CaseSensitive_WildcardMatch()
    {
        // Setup
        _router.SetCustomMapping("Claude-*", "uppercase-wildcard");

        // Act & Assert: Different case should not match
        var result1 = _router.ResolveModel("claude-3-opus");
        Assert.That(result1, Is.Not.EqualTo("uppercase-wildcard"));

        // Act & Assert: Same case should match
        var result2 = _router.ResolveModel("Claude-3-opus");
        Assert.That(result2, Is.EqualTo("uppercase-wildcard"));
    }

    [Test]
    public void WildcardMatch_CaseSensitive_DirectTest()
    {
        // Direct test of WildcardMatch method
        Assert.That(ModelRouter.WildcardMatch("ABC*", "ABC123"), Is.True);
        Assert.That(ModelRouter.WildcardMatch("ABC*", "abc123"), Is.False);
        Assert.That(ModelRouter.WildcardMatch("*XYZ", "testXYZ"), Is.True);
        Assert.That(ModelRouter.WildcardMatch("*XYZ", "testxyz"), Is.False);
    }

    #endregion

    #region Task 2.3.5: Test system default fallback

    [Test]
    public void ResolveModel_SystemDefault_Claude()
    {
        // Act
        var result = _router.ResolveModel("claude-3-5-sonnet-20241022");

        // Assert: Should use system default
        Assert.That(result, Is.EqualTo("claude-sonnet-4-5"));
    }

    [Test]
    public void ResolveModel_SystemDefault_OpenAI()
    {
        // Act & Assert: Various OpenAI models
        Assert.That(_router.ResolveModel("gpt-4"), Is.EqualTo("gemini-2.5-flash"));
        Assert.That(_router.ResolveModel("gpt-4-turbo"), Is.EqualTo("gemini-2.5-flash"));
        Assert.That(_router.ResolveModel("gpt-4o"), Is.EqualTo("gemini-2.5-flash"));
        Assert.That(_router.ResolveModel("gpt-4o-mini"), Is.EqualTo("gemini-2.5-flash"));
        Assert.That(_router.ResolveModel("gpt-3.5-turbo"), Is.EqualTo("gemini-2.5-flash"));
    }

    [Test]
    public void ResolveModel_SystemDefault_Gemini()
    {
        // Act & Assert
        Assert.That(_router.ResolveModel("gemini-3-pro-low"), Is.EqualTo("gemini-3-pro-preview"));
        Assert.That(_router.ResolveModel("gemini-3-pro-high"), Is.EqualTo("gemini-3-pro-preview"));
    }

    [Test]
    public void ResolveModel_Passthrough_GeminiPrefix()
    {
        // Act: Unknown gemini model should pass through
        var result = _router.ResolveModel("gemini-2.5-pro-custom");

        // Assert
        Assert.That(result, Is.EqualTo("gemini-2.5-pro-custom"));
    }

    [Test]
    public void ResolveModel_Passthrough_ThinkingModel()
    {
        // Act: Model containing "thinking" should pass through
        var result = _router.ResolveModel("claude-opus-4-5-thinking");

        // Assert
        Assert.That(result, Is.EqualTo("claude-opus-4-5-thinking"));
    }

    [Test]
    public void ResolveModel_Fallback_UnknownModel()
    {
        // Act: Completely unknown model
        var result = _router.ResolveModel("unknown-model-xyz");

        // Assert: Should fall back to default
        Assert.That(result, Is.EqualTo("claude-sonnet-4-5"));
    }

    [Test]
    public void ResolveModel_Fallback_EmptyString()
    {
        // Act
        var result = _router.ResolveModel("");

        // Assert
        Assert.That(result, Is.EqualTo("claude-sonnet-4-5"));
    }

    [Test]
    public void ResolveModel_CustomMapping_OverridesSystemDefault()
    {
        // Setup: Override a system default
        _router.SetCustomMapping("gpt-4", "custom-override");

        // Act
        var result = _router.ResolveModel("gpt-4");

        // Assert: Custom mapping should win
        Assert.That(result, Is.EqualTo("custom-override"));
    }

    #endregion

    #region Task 2.3.6: Test thread-safety with concurrent access

    [Test]
    public void ThreadSafety_ConcurrentSetAndResolve()
    {
        // Setup
        var router = new ModelRouter();
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();

        // Act: Run concurrent operations
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    // Mix of set, resolve, and remove operations
                    router.SetCustomMapping($"pattern-{index}", $"target-{index}");
                    router.ResolveModel($"pattern-{index}");
                    router.ResolveModel("gpt-4");
                    router.GetCustomMappings();
                    if (index % 2 == 0)
                        router.RemoveCustomMapping($"pattern-{index}");
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert: No exceptions should occur
        Assert.That(exceptions, Is.Empty, 
            $"Concurrent access caused exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");
    }

    [Test]
    public void ThreadSafety_ConcurrentWildcardMatching()
    {
        // Setup
        var router = new ModelRouter();
        router.SetCustomMapping("claude-*", "claude-target");
        router.SetCustomMapping("gpt-*", "gpt-target");
        router.SetCustomMapping("*-pro-*", "pro-target");

        var results = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = new List<Task>();

        // Act: Concurrent wildcard resolution
        for (int i = 0; i < 1000; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                results.Add(router.ResolveModel("claude-3-opus"));
                results.Add(router.ResolveModel("gpt-4-turbo"));
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert: All results should be consistent
        var claudeResults = results.Where(r => r == "claude-target").Count();
        var gptResults = results.Where(r => r == "gpt-target").Count();

        Assert.That(claudeResults, Is.EqualTo(1000), "All claude resolutions should be consistent");
        Assert.That(gptResults, Is.EqualTo(1000), "All gpt resolutions should be consistent");
    }

    [Test]
    public void ThreadSafety_ConcurrentMappingModification()
    {
        // Setup
        var router = new ModelRouter();
        var tasks = new List<Task>();
        var successCount = 0;

        // Act: Concurrent add/remove while resolving
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    router.SetCustomMapping("test-pattern", "test-target");
                    var result = router.ResolveModel("test-pattern");
                    router.RemoveCustomMapping("test-pattern");
                    
                    // Result should be either the custom mapping or fallback
                    if (result == "test-target" || result == "claude-sonnet-4-5")
                        Interlocked.Increment(ref successCount);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert: All operations should succeed without corruption
        Assert.That(successCount, Is.EqualTo(5000));
    }

    #endregion

    #region Additional edge case tests

    [Test]
    public void SetCustomMapping_ThrowsOnNullPattern()
    {
        Assert.Throws<ArgumentException>(() => _router.SetCustomMapping(null!, "target"));
    }

    [Test]
    public void SetCustomMapping_ThrowsOnEmptyPattern()
    {
        Assert.Throws<ArgumentException>(() => _router.SetCustomMapping("", "target"));
    }

    [Test]
    public void SetCustomMapping_ThrowsOnNullTarget()
    {
        Assert.Throws<ArgumentException>(() => _router.SetCustomMapping("pattern", null!));
    }

    [Test]
    public void SetCustomMapping_ThrowsOnEmptyTarget()
    {
        Assert.Throws<ArgumentException>(() => _router.SetCustomMapping("pattern", ""));
    }

    [Test]
    public void RemoveCustomMapping_NonExistent_DoesNotThrow()
    {
        // Should not throw when removing non-existent mapping
        Assert.DoesNotThrow(() => _router.RemoveCustomMapping("non-existent"));
    }

    [Test]
    public void GetCustomMappings_ReturnsCurrentMappings()
    {
        // Setup
        _router.SetCustomMapping("pattern1", "target1");
        _router.SetCustomMapping("pattern2", "target2");

        // Act
        var mappings = _router.GetCustomMappings();

        // Assert
        Assert.That(mappings.Count, Is.EqualTo(2));
        Assert.That(mappings["pattern1"], Is.EqualTo("target1"));
        Assert.That(mappings["pattern2"], Is.EqualTo("target2"));
    }

    [Test]
    public void GetCustomMappings_ReflectsChanges()
    {
        // Setup
        _router.SetCustomMapping("pattern1", "target1");
        var mappings1 = _router.GetCustomMappings();
        Assert.That(mappings1.Count, Is.EqualTo(1));

        // Act: Add another mapping
        _router.SetCustomMapping("pattern2", "target2");
        var mappings2 = _router.GetCustomMappings();

        // Assert: Should reflect the change
        Assert.That(mappings2.Count, Is.EqualTo(2));
    }

    [Test]
    public void WildcardMatch_SingleWildcardAtStart()
    {
        Assert.That(ModelRouter.WildcardMatch("*-opus", "claude-opus"), Is.True);
        Assert.That(ModelRouter.WildcardMatch("*-opus", "opus"), Is.False);
        Assert.That(ModelRouter.WildcardMatch("*-opus", "claude-sonnet"), Is.False);
    }

    [Test]
    public void WildcardMatch_SingleWildcardAtEnd()
    {
        Assert.That(ModelRouter.WildcardMatch("claude-*", "claude-opus"), Is.True);
        Assert.That(ModelRouter.WildcardMatch("claude-*", "claude-"), Is.True);
        Assert.That(ModelRouter.WildcardMatch("claude-*", "gpt-4"), Is.False);
    }

    [Test]
    public void WildcardMatch_SingleWildcardInMiddle()
    {
        Assert.That(ModelRouter.WildcardMatch("claude-*-opus", "claude-3-opus"), Is.True);
        Assert.That(ModelRouter.WildcardMatch("claude-*-opus", "claude-opus"), Is.False);
        Assert.That(ModelRouter.WildcardMatch("claude-*-opus", "claude-3-5-opus"), Is.True);
    }

    [Test]
    public void WildcardMatch_OnlyWildcard()
    {
        Assert.That(ModelRouter.WildcardMatch("*", "anything"), Is.True);
        Assert.That(ModelRouter.WildcardMatch("*", ""), Is.True);
    }

    [Test]
    public void WildcardMatch_NoWildcard()
    {
        Assert.That(ModelRouter.WildcardMatch("exact", "exact"), Is.True);
        Assert.That(ModelRouter.WildcardMatch("exact", "different"), Is.False);
    }

    #endregion
}
