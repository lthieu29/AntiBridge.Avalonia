namespace AntiBridge.Core.Models;

/// <summary>
/// Entry in the signature cache for thought signatures.
/// Maps SHA256 hash of thinking text to the cached signature.
/// </summary>
public class SignatureCacheEntry
{
    /// <summary>
    /// SHA256 hash of thinking text (cache key)
    /// </summary>
    public string TextHash { get; set; } = "";

    /// <summary>
    /// The cached thought signature from Antigravity API
    /// </summary>
    public string Signature { get; set; } = "";

    /// <summary>
    /// When this entry was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When this entry expires (based on TTL)
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}
