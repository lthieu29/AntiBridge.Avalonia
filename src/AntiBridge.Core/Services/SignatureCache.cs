using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AntiBridge.Core.Models;

namespace AntiBridge.Core.Services;

/// <summary>
/// Thread-safe cache for thought signatures with TTL-based expiration and LRU eviction.
/// Implements ISignatureCache interface for storing mapping from thinking text hash to signature.
/// </summary>
public class SignatureCache : ISignatureCache, IDisposable
{
    private readonly SignatureCacheOptions _options;
    private readonly ConcurrentDictionary<string, SignatureCacheEntry> _cache;
    private readonly LinkedList<string> _lruList; // For LRU tracking
    private readonly object _lruLock = new();
    private readonly Timer? _cleanupTimer;
    private readonly Func<DateTime> _timeProvider;
    private bool _disposed;

    /// <summary>
    /// Creates a new SignatureCache with the specified options.
    /// </summary>
    /// <param name="options">Cache configuration options</param>
    public SignatureCache(SignatureCacheOptions? options = null)
        : this(options, () => DateTime.UtcNow)
    {
    }

    /// <summary>
    /// Creates a new SignatureCache with the specified options and time provider.
    /// Used for testing with controllable time.
    /// </summary>
    /// <param name="options">Cache configuration options</param>
    /// <param name="timeProvider">Function to get current time</param>
    public SignatureCache(SignatureCacheOptions? options, Func<DateTime> timeProvider)
    {
        _options = options ?? new SignatureCacheOptions();
        _cache = new ConcurrentDictionary<string, SignatureCacheEntry>();
        _lruList = new LinkedList<string>();
        _timeProvider = timeProvider;

        // Start background cleanup timer if interval is positive
        if (_options.CleanupInterval > TimeSpan.Zero)
        {
            _cleanupTimer = new Timer(
                _ => CleanupExpired(),
                null,
                _options.CleanupInterval,
                _options.CleanupInterval);
        }
    }

    /// <summary>
    /// Get cached signature for thinking text.
    /// Uses SHA256 hash of the text as cache key.
    /// </summary>
    /// <param name="thinkingText">The thinking text to lookup</param>
    /// <returns>Cached signature or null if not found or expired</returns>
    public string? GetSignature(string thinkingText)
    {
        if (string.IsNullOrEmpty(thinkingText))
            return null;

        var hash = ComputeHash(thinkingText);
        
        if (_cache.TryGetValue(hash, out var entry))
        {
            var now = _timeProvider();
            
            // Check if entry has expired
            if (entry.ExpiresAt <= now)
            {
                // Remove expired entry
                RemoveEntry(hash);
                return null;
            }

            // Update LRU position (move to end = most recently used)
            UpdateLruPosition(hash);
            
            return entry.Signature;
        }

        return null;
    }

    /// <summary>
    /// Store signature for thinking text.
    /// Uses SHA256 hash of the text as cache key.
    /// Implements LRU eviction when cache is full.
    /// </summary>
    /// <param name="thinkingText">The thinking text</param>
    /// <param name="signature">The signature from Antigravity API</param>
    public void SetSignature(string thinkingText, string signature)
    {
        if (string.IsNullOrEmpty(thinkingText))
            return;

        // Validate signature format before caching
        if (!ValidateSignature(signature))
            return;

        var hash = ComputeHash(thinkingText);
        var now = _timeProvider();

        var entry = new SignatureCacheEntry
        {
            TextHash = hash,
            Signature = signature,
            CreatedAt = now,
            ExpiresAt = now.Add(_options.TTL)
        };

        // Check if we need to evict entries before adding
        EnsureCapacity();

        // Add or update the entry
        var isNew = !_cache.ContainsKey(hash);
        _cache[hash] = entry;

        // Update LRU tracking
        if (isNew)
        {
            AddToLru(hash);
        }
        else
        {
            UpdateLruPosition(hash);
        }
    }

    /// <summary>
    /// Validate signature format.
    /// Returns true if signature is non-empty string.
    /// </summary>
    /// <param name="signature">Signature to validate</param>
    /// <returns>True if valid format</returns>
    public bool ValidateSignature(string signature)
    {
        // Signature must be non-empty
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        // Signature should have reasonable length (not too short, not too long)
        // Typical thought signatures are base64-encoded and have substantial length
        if (signature.Length < 10)
            return false;

        // Signature should not exceed reasonable maximum length
        if (signature.Length > 10000)
            return false;

        return true;
    }

    /// <summary>
    /// Clear expired entries from the cache.
    /// Removes entries older than TTL.
    /// </summary>
    public void CleanupExpired()
    {
        var now = _timeProvider();
        var expiredKeys = new List<string>();

        foreach (var kvp in _cache)
        {
            if (kvp.Value.ExpiresAt <= now)
            {
                expiredKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            RemoveEntry(key);
        }
    }

    /// <summary>
    /// Gets the current number of entries in the cache.
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Computes SHA256 hash of the input text.
    /// </summary>
    /// <param name="text">Text to hash</param>
    /// <returns>Hex-encoded SHA256 hash</returns>
    private static string ComputeHash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Ensures cache has capacity for a new entry by evicting LRU entries if needed.
    /// </summary>
    private void EnsureCapacity()
    {
        while (_cache.Count >= _options.MaxEntries)
        {
            EvictLruEntry();
        }
    }

    /// <summary>
    /// Evicts the least recently used entry from the cache.
    /// </summary>
    private void EvictLruEntry()
    {
        string? keyToRemove = null;

        lock (_lruLock)
        {
            if (_lruList.Count > 0)
            {
                keyToRemove = _lruList.First?.Value;
                if (keyToRemove != null)
                {
                    _lruList.RemoveFirst();
                }
            }
        }

        if (keyToRemove != null)
        {
            _cache.TryRemove(keyToRemove, out _);
        }
    }

    /// <summary>
    /// Adds a key to the LRU list (at the end = most recently used).
    /// </summary>
    private void AddToLru(string key)
    {
        lock (_lruLock)
        {
            _lruList.AddLast(key);
        }
    }

    /// <summary>
    /// Updates the LRU position of a key (moves to end = most recently used).
    /// </summary>
    private void UpdateLruPosition(string key)
    {
        lock (_lruLock)
        {
            var node = _lruList.Find(key);
            if (node != null)
            {
                _lruList.Remove(node);
                _lruList.AddLast(key);
            }
        }
    }

    /// <summary>
    /// Removes an entry from both the cache and LRU list.
    /// </summary>
    private void RemoveEntry(string key)
    {
        _cache.TryRemove(key, out _);

        lock (_lruLock)
        {
            _lruList.Remove(key);
        }
    }

    /// <summary>
    /// Disposes the cache and stops the cleanup timer.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cleanupTimer?.Dispose();
        _cache.Clear();

        lock (_lruLock)
        {
            _lruList.Clear();
        }

        GC.SuppressFinalize(this);
    }
}
