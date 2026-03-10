using System.Collections.Concurrent;

namespace AntiBridge.Core.Services;

/// <summary>
/// Model router implementation with wildcard pattern support.
/// Thread-safe for concurrent access using ConcurrentDictionary.
/// </summary>
public class ModelRouter : IModelRouter
{
    private readonly ConcurrentDictionary<string, string> _customMappings = new();
    
    /// <summary>
    /// System default model mappings for common model names.
    /// </summary>
    private static readonly Dictionary<string, string> _systemDefaults = new()
    {
        // Claude mappings
        ["claude-3-5-sonnet-20241022"] = "claude-sonnet-4-5",
        ["claude-opus-4"] = "claude-opus-4-5-thinking",
        
        // OpenAI mappings
        ["gpt-4"] = "gemini-2.5-flash",
        ["gpt-4-turbo"] = "gemini-2.5-flash",
        ["gpt-4o"] = "gemini-2.5-flash",
        ["gpt-4o-mini"] = "gemini-2.5-flash",
        ["gpt-3.5-turbo"] = "gemini-2.5-flash",
        
        // Gemini mappings
        ["gemini-3-pro-low"] = "gemini-3-pro-preview",
        ["gemini-3-pro-high"] = "gemini-3-pro-preview",
    };

    /// <summary>
    /// Default fallback model when no mapping matches.
    /// </summary>
    private const string DefaultFallbackModel = "claude-sonnet-4-5";

    /// <inheritdoc />
    public string ResolveModel(string originalModel)
    {
        if (string.IsNullOrEmpty(originalModel))
            return DefaultFallbackModel;

        // 1. Exact match in custom mappings (highest priority)
        if (_customMappings.TryGetValue(originalModel, out var exactMatch))
            return exactMatch;

        // 2. Wildcard match (most specific wins)
        var bestMatch = FindBestWildcardMatch(originalModel);
        if (bestMatch != null)
            return bestMatch;

        // 3. System default mappings
        if (_systemDefaults.TryGetValue(originalModel, out var systemMatch))
            return systemMatch;

        // 4. Pass-through for known prefixes
        if (originalModel.StartsWith("gemini-") || originalModel.Contains("thinking"))
            return originalModel;

        // 5. Fallback
        return DefaultFallbackModel;
    }

    /// <inheritdoc />
    public void SetCustomMapping(string pattern, string target)
    {
        if (string.IsNullOrEmpty(pattern))
            throw new ArgumentException("Pattern cannot be null or empty", nameof(pattern));
        if (string.IsNullOrEmpty(target))
            throw new ArgumentException("Target cannot be null or empty", nameof(target));

        _customMappings[pattern] = target;
    }

    /// <inheritdoc />
    public void RemoveCustomMapping(string pattern)
    {
        _customMappings.TryRemove(pattern, out _);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetCustomMappings()
    {
        return _customMappings;
    }

    /// <summary>
    /// Find the best wildcard match for a model name.
    /// The most specific pattern (highest non-wildcard character count) wins.
    /// </summary>
    /// <param name="model">The model name to match</param>
    /// <returns>The target model name or null if no wildcard matches</returns>
    private string? FindBestWildcardMatch(string model)
    {
        string? bestTarget = null;
        int bestSpecificity = -1;

        foreach (var (pattern, target) in _customMappings)
        {
            // Skip non-wildcard patterns (already handled by exact match)
            if (!pattern.Contains('*'))
                continue;

            if (WildcardMatch(pattern, model))
            {
                // Specificity = number of non-wildcard characters
                int specificity = pattern.Length - pattern.Count(c => c == '*');
                if (specificity > bestSpecificity)
                {
                    bestSpecificity = specificity;
                    bestTarget = target;
                }
            }
        }

        return bestTarget;
    }

    /// <summary>
    /// Match a wildcard pattern against text.
    /// Supports multiple wildcards (*) in a single pattern.
    /// Matching is case-sensitive.
    /// </summary>
    /// <param name="pattern">The pattern with wildcards</param>
    /// <param name="text">The text to match</param>
    /// <returns>True if the pattern matches the text</returns>
    internal static bool WildcardMatch(string pattern, string text)
    {
        // No wildcards - exact match only
        var parts = pattern.Split('*');
        if (parts.Length == 1)
            return pattern == text;

        int textPos = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            
            // Empty part means wildcard at start, end, or consecutive wildcards
            if (string.IsNullOrEmpty(part))
                continue;

            if (i == 0)
            {
                // First part must match at the start
                if (!text.StartsWith(part, StringComparison.Ordinal))
                    return false;
                textPos = part.Length;
            }
            else if (i == parts.Length - 1)
            {
                // Last part must match at the end
                if (!text.EndsWith(part, StringComparison.Ordinal))
                    return false;
                // Also verify there's enough room for this part after textPos
                if (text.Length - textPos < part.Length)
                    return false;
            }
            else
            {
                // Middle parts must be found somewhere after current position
                var idx = text.IndexOf(part, textPos, StringComparison.Ordinal);
                if (idx < 0)
                    return false;
                textPos = idx + part.Length;
            }
        }

        return true;
    }
}
