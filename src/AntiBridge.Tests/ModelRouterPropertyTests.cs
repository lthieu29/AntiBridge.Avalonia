using AntiBridge.Core.Services;
using FsCheck;
using NUnit.Framework;
using PropertyAttribute = FsCheck.NUnit.PropertyAttribute;

namespace AntiBridge.Tests;

/// <summary>
/// Property-based tests for ModelRouter.
/// Feature: antibridge-feature-port
/// </summary>
[TestFixture]
public class ModelRouterPropertyTests
{
    #region Custom Generators

    /// <summary>
    /// Generator for valid model names (non-empty strings with typical model name characters).
    /// </summary>
    private static Arbitrary<string> ModelNameArbitrary()
    {
        // Model names typically contain alphanumeric chars, hyphens, underscores, and dots
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();
        
        return Gen.Elements(validChars)
            .ArrayOf()
            .Where(arr => arr.Length >= 1 && arr.Length <= 100)
            .Select(arr => new string(arr))
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for wildcard patterns (model names that may contain * wildcards).
    /// </summary>
    private static Arbitrary<string> WildcardPatternArbitrary()
    {
        // Pattern chars include wildcards
        var patternChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.*".ToCharArray();
        
        return Gen.Elements(patternChars)
            .ArrayOf()
            .Where(arr => arr.Length >= 1 && arr.Length <= 50)
            .Select(arr => new string(arr))
            // Ensure at least one non-wildcard character for valid patterns
            .Where(s => s.Any(c => c != '*'))
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for target model names (non-empty, no wildcards).
    /// </summary>
    private static Arbitrary<string> TargetModelArbitrary()
    {
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();
        
        return Gen.Elements(validChars)
            .ArrayOf()
            .Where(arr => arr.Length >= 1 && arr.Length <= 50)
            .Select(arr => new string(arr))
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for custom mapping dictionaries.
    /// </summary>
    private static Arbitrary<Dictionary<string, string>> CustomMappingsArbitrary()
    {
        var patternGen = WildcardPatternArbitrary().Generator;
        var targetGen = TargetModelArbitrary().Generator;
        
        var pairGen = Gen.Zip(patternGen, targetGen);
        
        return Gen.ListOf(pairGen)
            .Where(list => list.Count() <= 20) // Limit to reasonable number of mappings
            .Select(pairs => pairs
                .GroupBy(p => p.Item1)
                .Select(g => g.First())
                .ToDictionary(p => p.Item1, p => p.Item2))
            .ToArbitrary();
    }

    #endregion

    #region Property 9.1.1: Determinism - same input always produces same output

    /// <summary>
    /// Property: Determinism - same input always produces same output
    /// 
    /// For any input model name and fixed custom mappings, calling ResolveModel
    /// multiple times with the same input MUST return the same output.
    /// 
    /// **Feature: antibridge-feature-port, Property 1: Model Router Determinism**
    /// **Validates: Requirements 9.1.1**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 50)]
    public Property Determinism_SameInputAlwaysProducesSameOutput()
    {
        var modelNameGen = ModelNameArbitrary().Generator;
        var mappingsGen = CustomMappingsArbitrary().Generator;

        return Prop.ForAll(
            modelNameGen.ToArbitrary(),
            mappingsGen.ToArbitrary(),
            (modelName, customMappings) =>
            {
                // Arrange: Create router with custom mappings
                var router = new ModelRouter();
                foreach (var (pattern, target) in customMappings)
                {
                    router.SetCustomMapping(pattern, target);
                }

                // Act: Call ResolveModel multiple times with the same input
                var result1 = router.ResolveModel(modelName);
                var result2 = router.ResolveModel(modelName);
                var result3 = router.ResolveModel(modelName);

                // Assert: All results must be identical
                var allEqual = result1 == result2 && result2 == result3;

                return allEqual
                    .Label($"ResolveModel('{modelName}') returned different results: " +
                           $"'{result1}', '{result2}', '{result3}'")
                    .Classify(customMappings.Count == 0, "no custom mappings")
                    .Classify(customMappings.Count > 0 && customMappings.Count <= 5, "few custom mappings")
                    .Classify(customMappings.Count > 5, "many custom mappings")
                    .Classify(customMappings.Any(m => m.Key.Contains('*')), "has wildcard patterns")
                    .Classify(!customMappings.Any(m => m.Key.Contains('*')), "no wildcard patterns");
            });
    }

    /// <summary>
    /// Property: Determinism with empty custom mappings
    /// 
    /// For any input model name with no custom mappings, calling ResolveModel
    /// multiple times MUST return the same output (using system defaults or fallback).
    /// 
    /// **Feature: antibridge-feature-port, Property 1: Model Router Determinism**
    /// **Validates: Requirements 9.1.1**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 50)]
    public Property Determinism_EmptyMappings_SameInputAlwaysProducesSameOutput()
    {
        var modelNameGen = ModelNameArbitrary().Generator;

        return Prop.ForAll(
            modelNameGen.ToArbitrary(),
            modelName =>
            {
                // Arrange: Create router with no custom mappings
                var router = new ModelRouter();

                // Act: Call ResolveModel multiple times
                var result1 = router.ResolveModel(modelName);
                var result2 = router.ResolveModel(modelName);
                var result3 = router.ResolveModel(modelName);

                // Assert: All results must be identical
                var allEqual = result1 == result2 && result2 == result3;

                return allEqual
                    .Label($"ResolveModel('{modelName}') with empty mappings returned different results: " +
                           $"'{result1}', '{result2}', '{result3}'")
                    .Classify(modelName.StartsWith("gemini-"), "gemini prefix (passthrough)")
                    .Classify(modelName.Contains("thinking"), "contains thinking (passthrough)")
                    .Classify(modelName.StartsWith("gpt-"), "gpt prefix")
                    .Classify(modelName.StartsWith("claude-"), "claude prefix");
            });
    }

    /// <summary>
    /// Property: Determinism across multiple router instances
    /// 
    /// For any input model name and fixed custom mappings, two separate router instances
    /// with identical mappings MUST return the same output.
    /// 
    /// **Feature: antibridge-feature-port, Property 1: Model Router Determinism**
    /// **Validates: Requirements 9.1.1**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 50)]
    public Property Determinism_MultipleRouterInstances_SameOutput()
    {
        var modelNameGen = ModelNameArbitrary().Generator;
        var mappingsGen = CustomMappingsArbitrary().Generator;

        return Prop.ForAll(
            modelNameGen.ToArbitrary(),
            mappingsGen.ToArbitrary(),
            (modelName, customMappings) =>
            {
                // Arrange: Create two separate router instances with identical mappings
                var router1 = new ModelRouter();
                var router2 = new ModelRouter();
                
                foreach (var (pattern, target) in customMappings)
                {
                    router1.SetCustomMapping(pattern, target);
                    router2.SetCustomMapping(pattern, target);
                }

                // Act: Call ResolveModel on both routers
                var result1 = router1.ResolveModel(modelName);
                var result2 = router2.ResolveModel(modelName);

                // Assert: Both routers must return the same result
                var areEqual = result1 == result2;

                return areEqual
                    .Label($"Two routers with identical mappings returned different results for '{modelName}': " +
                           $"Router1='{result1}', Router2='{result2}'")
                    .Classify(customMappings.Count == 0, "no custom mappings")
                    .Classify(customMappings.Count > 0, "has custom mappings");
            });
    }

    /// <summary>
    /// Property: Determinism with populated custom mappings (exact matches)
    /// 
    /// For any input model name that has an exact match in custom mappings,
    /// calling ResolveModel multiple times MUST return the same mapped target.
    /// 
    /// **Feature: antibridge-feature-port, Property 1: Model Router Determinism**
    /// **Validates: Requirements 9.1.1**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 50)]
    public Property Determinism_ExactMatchMappings_SameInputAlwaysProducesSameOutput()
    {
        var modelNameGen = ModelNameArbitrary().Generator;
        var targetGen = TargetModelArbitrary().Generator;

        return Prop.ForAll(
            modelNameGen.ToArbitrary(),
            targetGen.ToArbitrary(),
            (modelName, target) =>
            {
                // Arrange: Create router with exact match mapping for the model name
                var router = new ModelRouter();
                router.SetCustomMapping(modelName, target);

                // Act: Call ResolveModel multiple times
                var result1 = router.ResolveModel(modelName);
                var result2 = router.ResolveModel(modelName);
                var result3 = router.ResolveModel(modelName);

                // Assert: All results must be identical and equal to the target
                var allEqual = result1 == result2 && result2 == result3;
                var matchesTarget = result1 == target;

                return (allEqual && matchesTarget)
                    .Label($"ResolveModel('{modelName}') should consistently return '{target}', " +
                           $"but got: '{result1}', '{result2}', '{result3}'")
                    .Classify(modelName.Length < 10, "short model name")
                    .Classify(modelName.Length >= 10 && modelName.Length < 30, "medium model name")
                    .Classify(modelName.Length >= 30, "long model name");
            });
    }

    #endregion

    #region Property 9.1.2: Wildcard specificity - most specific pattern wins

    /// <summary>
    /// Property: Wildcard specificity - most specific pattern wins
    /// 
    /// When multiple wildcard patterns match the same model name, the pattern with
    /// the highest specificity (most non-wildcard characters) MUST win.
    /// Specificity = pattern.Length - pattern.Count(c => c == '*')
    /// 
    /// **Feature: antibridge-feature-port, Property 2: Wildcard Specificity**
    /// **Validates: Requirements 9.1.2**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 30)]
    public Property WildcardSpecificity_MostSpecificPatternWins()
    {
        // Generate a base model name and create patterns with varying specificity
        var baseNameGen = Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray())
            .ArrayOf()
            .Where(arr => arr.Length >= 5 && arr.Length <= 20)
            .Select(arr => new string(arr));

        var targetGen = TargetModelArbitrary().Generator;

        return Prop.ForAll(
            baseNameGen.ToArbitrary(),
            targetGen.ToArbitrary(),
            targetGen.ToArbitrary(),
            (baseName, lessSpecificTarget, moreSpecificTarget) =>
            {
                // Skip if targets are the same (can't distinguish which pattern won)
                if (lessSpecificTarget == moreSpecificTarget)
                    return true.Label("Skipped: targets are identical");

                // Create two patterns that both match the base name:
                // Less specific: first char + * (e.g., "a*" for "abcdef")
                // More specific: first 3 chars + * (e.g., "abc*" for "abcdef")
                var lessSpecificPattern = baseName[0] + "*";
                var moreSpecificPattern = baseName.Substring(0, Math.Min(3, baseName.Length)) + "*";

                // Calculate specificities
                int lessSpecificity = lessSpecificPattern.Length - lessSpecificPattern.Count(c => c == '*');
                int moreSpecificity = moreSpecificPattern.Length - moreSpecificPattern.Count(c => c == '*');

                // Ensure more specific pattern actually has higher specificity
                if (moreSpecificity <= lessSpecificity)
                    return true.Label("Skipped: patterns have equal or inverted specificity");

                // Arrange: Create router with both patterns
                var router = new ModelRouter();
                router.SetCustomMapping(lessSpecificPattern, lessSpecificTarget);
                router.SetCustomMapping(moreSpecificPattern, moreSpecificTarget);

                // Act: Resolve the model
                var result = router.ResolveModel(baseName);

                // Assert: The more specific pattern should win
                var moreSpecificWins = result == moreSpecificTarget;

                return moreSpecificWins
                    .Label($"Expected more specific pattern '{moreSpecificPattern}' (specificity={moreSpecificity}) " +
                           $"to win over '{lessSpecificPattern}' (specificity={lessSpecificity}) " +
                           $"for model '{baseName}'. Expected '{moreSpecificTarget}', got '{result}'")
                    .Classify(baseName.Length < 10, "short model name")
                    .Classify(baseName.Length >= 10, "longer model name");
            });
    }

    /// <summary>
    /// Property: Wildcard specificity with multiple wildcards
    /// 
    /// When patterns contain multiple wildcards, specificity is still calculated as
    /// pattern.Length - pattern.Count(c => c == '*'), and the most specific wins.
    /// 
    /// **Feature: antibridge-feature-port, Property 2: Wildcard Specificity**
    /// **Validates: Requirements 9.1.2**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 30)]
    public Property WildcardSpecificity_MultipleWildcards_MostSpecificWins()
    {
        var targetGen = TargetModelArbitrary().Generator;

        return Prop.ForAll(
            targetGen.ToArbitrary(),
            targetGen.ToArbitrary(),
            (lessSpecificTarget, moreSpecificTarget) =>
            {
                // Skip if targets are the same
                if (lessSpecificTarget == moreSpecificTarget)
                    return true.Label("Skipped: targets are identical");

                // Test with a model name like "claude-3-sonnet-20241022"
                var modelName = "claude-3-sonnet-20241022";

                // Less specific: "claude-*-*" (specificity = 13 - 2 = 11)
                var lessSpecificPattern = "claude-*-*";
                // More specific: "claude-*-sonnet-*" (specificity = 17 - 2 = 15)
                var moreSpecificPattern = "claude-*-sonnet-*";

                int lessSpecificity = lessSpecificPattern.Length - lessSpecificPattern.Count(c => c == '*');
                int moreSpecificity = moreSpecificPattern.Length - moreSpecificPattern.Count(c => c == '*');

                // Arrange: Create router with both patterns
                var router = new ModelRouter();
                router.SetCustomMapping(lessSpecificPattern, lessSpecificTarget);
                router.SetCustomMapping(moreSpecificPattern, moreSpecificTarget);

                // Act: Resolve the model
                var result = router.ResolveModel(modelName);

                // Assert: The more specific pattern should win
                var moreSpecificWins = result == moreSpecificTarget;

                return moreSpecificWins
                    .Label($"Expected more specific pattern '{moreSpecificPattern}' (specificity={moreSpecificity}) " +
                           $"to win over '{lessSpecificPattern}' (specificity={lessSpecificity}) " +
                           $"for model '{modelName}'. Expected '{moreSpecificTarget}', got '{result}'")
                    .Classify(true, $"less={lessSpecificity}, more={moreSpecificity}");
            });
    }

    /// <summary>
    /// Property: Wildcard specificity calculation is correct
    /// 
    /// Verifies that specificity is correctly calculated as:
    /// specificity = pattern.Length - pattern.Count(c => c == '*')
    /// 
    /// **Feature: antibridge-feature-port, Property 2: Wildcard Specificity**
    /// **Validates: Requirements 9.1.2**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 30)]
    public Property WildcardSpecificity_CalculationIsCorrect()
    {
        // Generate patterns with known wildcard counts
        var prefixGen = Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray())
            .ArrayOf()
            .Where(arr => arr.Length >= 2 && arr.Length <= 10)
            .Select(arr => new string(arr));

        var targetGen = TargetModelArbitrary().Generator;

        // Combine generators into a tuple
        var combinedGen = Gen.Zip(prefixGen, targetGen, targetGen)
            .Select(t => (prefix: new string(t.Item1), target1: t.Item2, target2: t.Item3));

        return Prop.ForAll(
            combinedGen.ToArbitrary(),
            tuple =>
            {
                var (prefix, target1, target2) = tuple;

                // Skip if targets are the same
                if (target1 == target2)
                    return true.Label("Skipped: targets are identical");

                // Create a model name that will match both patterns
                var modelName = prefix + "-extra-content-here";

                // Pattern 1: prefix + single wildcard (higher specificity)
                var pattern1 = prefix + "*";
                // Pattern 2: shorter prefix + wildcard (lower specificity)
                var shorterPrefix = prefix.Substring(0, Math.Max(1, prefix.Length / 2));
                var pattern2 = shorterPrefix + "*";

                int specificity1 = pattern1.Length - pattern1.Count(c => c == '*');
                int specificity2 = pattern2.Length - pattern2.Count(c => c == '*');

                // Ensure pattern1 has higher specificity
                if (specificity1 <= specificity2)
                    return true.Label("Skipped: pattern1 doesn't have higher specificity");

                // Arrange: Create router with both patterns
                var router = new ModelRouter();
                router.SetCustomMapping(pattern1, target1);
                router.SetCustomMapping(pattern2, target2);

                // Act: Resolve the model
                var result = router.ResolveModel(modelName);

                // Assert: The pattern with higher specificity should win
                var higherSpecificityWins = result == target1;

                return higherSpecificityWins
                    .Label($"Expected pattern '{pattern1}' (specificity={specificity1}) " +
                           $"to win over '{pattern2}' (specificity={specificity2}) " +
                           $"for model '{modelName}'. Expected '{target1}', got '{result}'")
                    .Classify(specificity1 - specificity2 == 1, "specificity diff = 1")
                    .Classify(specificity1 - specificity2 > 1, "specificity diff > 1");
            });
    }

    /// <summary>
    /// Property: Wildcard specificity - exact match beats any wildcard
    /// 
    /// An exact match (no wildcards) should always beat any wildcard pattern,
    /// regardless of the wildcard pattern's specificity.
    /// 
    /// **Feature: antibridge-feature-port, Property 2: Wildcard Specificity**
    /// **Validates: Requirements 9.1.2**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 30)]
    public Property WildcardSpecificity_ExactMatchBeatsWildcard()
    {
        var modelNameGen = ModelNameArbitrary().Generator;
        var targetGen = TargetModelArbitrary().Generator;

        return Prop.ForAll(
            modelNameGen.ToArbitrary(),
            targetGen.ToArbitrary(),
            targetGen.ToArbitrary(),
            (modelName, exactTarget, wildcardTarget) =>
            {
                // Skip if targets are the same or model name is too short
                if (exactTarget == wildcardTarget || modelName.Length < 2)
                    return true.Label("Skipped: targets identical or model too short");

                // Create a wildcard pattern that matches the model name
                var wildcardPattern = modelName[0] + "*";

                // Arrange: Create router with both exact and wildcard mappings
                var router = new ModelRouter();
                router.SetCustomMapping(modelName, exactTarget);  // Exact match
                router.SetCustomMapping(wildcardPattern, wildcardTarget);  // Wildcard match

                // Act: Resolve the model
                var result = router.ResolveModel(modelName);

                // Assert: Exact match should always win
                var exactMatchWins = result == exactTarget;

                return exactMatchWins
                    .Label($"Expected exact match '{modelName}' -> '{exactTarget}' " +
                           $"to beat wildcard '{wildcardPattern}' -> '{wildcardTarget}'. " +
                           $"Got '{result}'")
                    .Classify(modelName.Length < 10, "short model name")
                    .Classify(modelName.Length >= 10, "longer model name");
            });
    }

    /// <summary>
    /// Property: Wildcard specificity with suffix patterns
    /// 
    /// Tests that specificity works correctly with patterns that have wildcards
    /// at the beginning (suffix patterns like "*-sonnet").
    /// 
    /// **Feature: antibridge-feature-port, Property 2: Wildcard Specificity**
    /// **Validates: Requirements 9.1.2**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 30)]
    public Property WildcardSpecificity_SuffixPatterns_MostSpecificWins()
    {
        var targetGen = TargetModelArbitrary().Generator;

        return Prop.ForAll(
            targetGen.ToArbitrary(),
            targetGen.ToArbitrary(),
            (lessSpecificTarget, moreSpecificTarget) =>
            {
                // Skip if targets are the same
                if (lessSpecificTarget == moreSpecificTarget)
                    return true.Label("Skipped: targets are identical");

                // Test with a model name like "claude-3-5-sonnet"
                var modelName = "claude-3-5-sonnet";

                // Less specific: "*-sonnet" (specificity = 8 - 1 = 7)
                var lessSpecificPattern = "*-sonnet";
                // More specific: "*-5-sonnet" (specificity = 10 - 1 = 9)
                var moreSpecificPattern = "*-5-sonnet";

                int lessSpecificity = lessSpecificPattern.Length - lessSpecificPattern.Count(c => c == '*');
                int moreSpecificity = moreSpecificPattern.Length - moreSpecificPattern.Count(c => c == '*');

                // Arrange: Create router with both patterns
                var router = new ModelRouter();
                router.SetCustomMapping(lessSpecificPattern, lessSpecificTarget);
                router.SetCustomMapping(moreSpecificPattern, moreSpecificTarget);

                // Act: Resolve the model
                var result = router.ResolveModel(modelName);

                // Assert: The more specific pattern should win
                var moreSpecificWins = result == moreSpecificTarget;

                return moreSpecificWins
                    .Label($"Expected more specific pattern '{moreSpecificPattern}' (specificity={moreSpecificity}) " +
                           $"to win over '{lessSpecificPattern}' (specificity={lessSpecificity}) " +
                           $"for model '{modelName}'. Expected '{moreSpecificTarget}', got '{result}'")
                    .Classify(true, $"suffix pattern test");
            });
    }

    #endregion
}
