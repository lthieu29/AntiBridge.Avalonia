namespace AntiBridge.Core.Services;

/// <summary>
/// Interface for model name routing with wildcard pattern support.
/// Routes incoming model names to target model names using exact match,
/// wildcard patterns, and system defaults.
/// </summary>
public interface IModelRouter
{
    /// <summary>
    /// Resolve the target model name for a given input model.
    /// Resolution priority:
    /// 1. Exact match in custom mappings (highest priority)
    /// 2. Wildcard match (most specific pattern wins)
    /// 3. System default mappings
    /// 4. Pass-through for known prefixes (gemini-, *thinking*)
    /// 5. Fallback to default model
    /// </summary>
    /// <param name="originalModel">The original model name from the request</param>
    /// <returns>The resolved target model name</returns>
    string ResolveModel(string originalModel);

    /// <summary>
    /// Add or update a custom model mapping.
    /// Supports exact matches and wildcard patterns (using * character).
    /// </summary>
    /// <param name="pattern">The pattern to match (can include * wildcards)</param>
    /// <param name="target">The target model name to route to</param>
    void SetCustomMapping(string pattern, string target);

    /// <summary>
    /// Remove a custom model mapping.
    /// </summary>
    /// <param name="pattern">The pattern to remove</param>
    void RemoveCustomMapping(string pattern);

    /// <summary>
    /// Get all current custom mappings.
    /// </summary>
    /// <returns>Read-only dictionary of pattern to target mappings</returns>
    IReadOnlyDictionary<string, string> GetCustomMappings();
}
