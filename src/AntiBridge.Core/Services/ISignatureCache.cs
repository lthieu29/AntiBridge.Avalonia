namespace AntiBridge.Core.Services;

/// <summary>
/// Cache for thought signatures to avoid validation errors.
/// Stores mapping from thinking text hash to signature.
/// </summary>
public interface ISignatureCache
{
    /// <summary>
    /// Get cached signature for thinking text.
    /// Uses SHA256 hash of the text as cache key.
    /// </summary>
    /// <param name="thinkingText">The thinking text to lookup</param>
    /// <returns>Cached signature or null if not found</returns>
    string? GetSignature(string thinkingText);

    /// <summary>
    /// Store signature for thinking text.
    /// Uses SHA256 hash of the text as cache key.
    /// </summary>
    /// <param name="thinkingText">The thinking text</param>
    /// <param name="signature">The signature from Antigravity API</param>
    void SetSignature(string thinkingText, string signature);

    /// <summary>
    /// Validate signature format.
    /// Returns true if signature has valid format (non-empty, expected pattern).
    /// </summary>
    /// <param name="signature">Signature to validate</param>
    /// <returns>True if valid format</returns>
    bool ValidateSignature(string signature);

    /// <summary>
    /// Clear expired entries from the cache.
    /// Removes entries older than TTL.
    /// </summary>
    void CleanupExpired();
}

/// <summary>
/// Configuration options for SignatureCache.
/// </summary>
public class SignatureCacheOptions
{
    /// <summary>
    /// Time-to-live for cache entries (default: 1 hour)
    /// </summary>
    public TimeSpan TTL { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Maximum number of entries in cache (default: 10000)
    /// </summary>
    public int MaxEntries { get; set; } = 10000;

    /// <summary>
    /// Interval for automatic cleanup of expired entries (default: 5 minutes)
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}
