using System.Text;
using System.Text.Json.Nodes;
using AntiBridge.Core.Services;
using AntiBridge.Core.Translator;
using FsCheck;
using NUnit.Framework;
using PropertyAttribute = FsCheck.NUnit.PropertyAttribute;

namespace AntiBridge.Tests;

/// <summary>
/// Property-based tests for SignatureCache.
/// Feature: antibridge-advanced-features
/// </summary>
[TestFixture]
public class SignatureCachePropertyTests
{
    /// <summary>
    /// Custom generator for non-empty thinking text strings.
    /// </summary>
    private static Arbitrary<string> NonEmptyThinkingTextArbitrary()
    {
        return Arb.Default.NonEmptyString()
            .Generator
            .Select(nes => nes.Get)
            .ToArbitrary();
    }

    /// <summary>
    /// Custom generator for valid signatures (strings with length >= 10).
    /// </summary>
    private static Arbitrary<string> ValidSignatureArbitrary()
    {
        return Gen.Elements(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=".ToCharArray())
            .ArrayOf()
            .Where(arr => arr.Length >= 10 && arr.Length <= 1000)
            .Select(arr => new string(arr))
            .ToArbitrary();
    }

    /// <summary>
    /// Property 1: Signature Cache Round-Trip
    /// 
    /// For any thinking text and valid signature, if the signature is stored in the cache
    /// with that thinking text, then lookup with the same thinking text must return the same signature.
    /// 
    /// **Feature: antibridge-advanced-features, Property 1: Signature Cache Round-Trip**
    /// **Validates: Requirements 1.1, 1.7**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 10, EndSize = 100)]
    public Property SignatureCacheRoundTrip_StoredSignature_CanBeRetrieved()
    {
        var thinkingTextGen = NonEmptyThinkingTextArbitrary().Generator;
        var signatureGen = ValidSignatureArbitrary().Generator;

        return Prop.ForAll(
            thinkingTextGen.ToArbitrary(),
            signatureGen.ToArbitrary(),
            (thinkingText, signature) =>
            {
                // Arrange: Create a fresh cache with no automatic cleanup
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromHours(1),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero // Disable automatic cleanup
                };
                using var cache = new SignatureCache(options);

                // Act: Store the signature
                cache.SetSignature(thinkingText, signature);

                // Act: Retrieve the signature
                var retrievedSignature = cache.GetSignature(thinkingText);

                // Assert: Retrieved signature must equal stored signature
                return (retrievedSignature == signature)
                    .Label($"Expected signature '{signature}' but got '{retrievedSignature}'")
                    .Classify(thinkingText.Length < 50, "short thinking text")
                    .Classify(thinkingText.Length >= 50 && thinkingText.Length < 200, "medium thinking text")
                    .Classify(thinkingText.Length >= 200, "long thinking text")
                    .Classify(signature.Length < 50, "short signature")
                    .Classify(signature.Length >= 50, "long signature");
            });
    }

    /// <summary>
    /// Additional test to verify round-trip works with multiple entries.
    /// This ensures the cache correctly handles multiple distinct entries.
    /// 
    /// **Feature: antibridge-advanced-features, Property 1: Signature Cache Round-Trip**
    /// **Validates: Requirements 1.1, 1.7**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 10, EndSize = 100)]
    public Property SignatureCacheRoundTrip_MultipleEntries_AllCanBeRetrieved()
    {
        var thinkingTextGen = NonEmptyThinkingTextArbitrary().Generator;
        var signatureGen = ValidSignatureArbitrary().Generator;

        // Generate pairs of (thinkingText, signature)
        var pairGen = Gen.Zip(thinkingTextGen, signatureGen);
        var pairsGen = Gen.ListOf(pairGen)
            .Where(list => list.Count() > 0 && list.Count() <= 50);

        return Prop.ForAll(
            pairsGen.ToArbitrary(),
            pairs =>
            {
                // Arrange: Create a fresh cache
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromHours(1),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero
                };
                using var cache = new SignatureCache(options);

                // Make pairs unique by thinking text (last one wins for duplicates)
                var uniquePairs = pairs
                    .GroupBy(p => p.Item1)
                    .Select(g => g.Last())
                    .ToList();

                // Act: Store all signatures
                foreach (var pair in uniquePairs)
                {
                    cache.SetSignature(pair.Item1, pair.Item2);
                }

                // Assert: All signatures can be retrieved correctly
                var allMatch = uniquePairs.All(pair =>
                {
                    var retrieved = cache.GetSignature(pair.Item1);
                    return retrieved == pair.Item2;
                });

                return allMatch
                    .Label($"Not all {uniquePairs.Count} entries could be retrieved correctly")
                    .Classify(uniquePairs.Count == 1, "single entry")
                    .Classify(uniquePairs.Count > 1 && uniquePairs.Count <= 10, "few entries")
                    .Classify(uniquePairs.Count > 10, "many entries");
            });
    }

    /// <summary>
    /// Property 4: Cache TTL Expiration
    /// 
    /// For any cache with entries having different timestamps, after cleanup is triggered,
    /// only entries with age less than TTL should remain in the cache.
    /// 
    /// **Feature: antibridge-advanced-features, Property 4: Cache TTL Expiration**
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 10, EndSize = 100)]
    public Property CacheTTLExpiration_AfterCleanup_OnlyNonExpiredEntriesRemain()
    {
        var thinkingTextGen = NonEmptyThinkingTextArbitrary().Generator;
        var signatureGen = ValidSignatureArbitrary().Generator;
        var creationTimeOffsetGen = Gen.Choose(0, 120); // Creation time offset: 0 to 120 minutes from base time

        // Generate a list of entries with their relative creation times (in minutes)
        // Each entry is (thinkingText, signature, creationTimeOffsetMinutes)
        var entryGen = from text in thinkingTextGen
                       from sig in signatureGen
                       from offset in creationTimeOffsetGen
                       select (Text: text, Signature: sig, Offset: offset);
        
        var entriesGen = Gen.ListOf(entryGen)
            .Where(list => list.Count() >= 1 && list.Count() <= 30);

        // Generate TTL in minutes (10 to 60 minutes)
        var ttlMinutesGen = Gen.Choose(10, 60);
        
        // Generate time advance in minutes (0 to 180 minutes)
        var timeAdvanceMinutesGen = Gen.Choose(0, 180);

        return Prop.ForAll(
            entriesGen.ToArbitrary(),
            ttlMinutesGen.ToArbitrary(),
            timeAdvanceMinutesGen.ToArbitrary(),
            (entries, ttlMinutes, timeAdvanceMinutes) =>
            {
                // Arrange: Set up controllable time
                var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var currentTime = baseTime;
                
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromMinutes(ttlMinutes),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero // Disable automatic cleanup
                };
                
                using var cache = new SignatureCache(options, () => currentTime);

                // Make entries unique by thinking text
                var uniqueEntries = entries
                    .GroupBy(e => e.Text)
                    .Select(g => g.First())
                    .ToList();

                // Act: Store entries at different times
                foreach (var entry in uniqueEntries)
                {
                    // Set time to when this entry should be created
                    currentTime = baseTime.AddMinutes(entry.Offset);
                    cache.SetSignature(entry.Text, entry.Signature);
                }

                // Advance time to the cleanup point
                var cleanupTime = baseTime.AddMinutes(timeAdvanceMinutes);
                currentTime = cleanupTime;

                // Trigger cleanup
                cache.CleanupExpired();

                // Assert: Verify only non-expired entries remain
                // An entry is expired if: cleanupTime >= creationTime + TTL
                // Which means: cleanupTime - creationTime >= TTL
                // Or: timeAdvanceMinutes - creationTimeOffset >= ttlMinutes
                
                var allCorrect = uniqueEntries.All(entry =>
                {
                    var creationTimeOffset = entry.Offset;
                    var entryAge = timeAdvanceMinutes - creationTimeOffset;
                    var shouldBeExpired = entryAge >= ttlMinutes;
                    
                    var retrieved = cache.GetSignature(entry.Text);
                    var isInCache = retrieved != null;
                    
                    // If should be expired, it should NOT be in cache
                    // If should NOT be expired, it SHOULD be in cache with correct value
                    if (shouldBeExpired)
                    {
                        return !isInCache;
                    }
                    else
                    {
                        return isInCache && retrieved == entry.Signature;
                    }
                });

                // Count expected expired and non-expired for classification
                var expiredCount = uniqueEntries.Count(e => 
                    (timeAdvanceMinutes - e.Offset) >= ttlMinutes);
                var nonExpiredCount = uniqueEntries.Count - expiredCount;

                return allCorrect
                    .Label($"TTL={ttlMinutes}min, TimeAdvance={timeAdvanceMinutes}min, " +
                           $"Entries={uniqueEntries.Count}, Expected expired={expiredCount}, non-expired={nonExpiredCount}")
                    .Classify(expiredCount == 0, "no entries expired")
                    .Classify(expiredCount > 0 && nonExpiredCount > 0, "mixed expiration")
                    .Classify(nonExpiredCount == 0, "all entries expired")
                    .Classify(uniqueEntries.Count == 1, "single entry")
                    .Classify(uniqueEntries.Count > 1 && uniqueEntries.Count <= 10, "few entries")
                    .Classify(uniqueEntries.Count > 10, "many entries");
            });
    }

    /// <summary>
    /// Additional property test for TTL expiration: entries created at the exact TTL boundary.
    /// Verifies that entries exactly at TTL age are considered expired.
    /// 
    /// **Feature: antibridge-advanced-features, Property 4: Cache TTL Expiration**
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 10, EndSize = 100)]
    public Property CacheTTLExpiration_AtExactTTLBoundary_EntryIsExpired()
    {
        var thinkingTextGen = NonEmptyThinkingTextArbitrary().Generator;
        var signatureGen = ValidSignatureArbitrary().Generator;
        var ttlMinutesGen = Gen.Choose(1, 120);

        return Prop.ForAll(
            thinkingTextGen.ToArbitrary(),
            signatureGen.ToArbitrary(),
            ttlMinutesGen.ToArbitrary(),
            (thinkingText, signature, ttlMinutes) =>
            {
                // Arrange: Set up controllable time
                var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var currentTime = baseTime;
                
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromMinutes(ttlMinutes),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero
                };
                
                using var cache = new SignatureCache(options, () => currentTime);

                // Act: Store entry at base time
                cache.SetSignature(thinkingText, signature);

                // Advance time to exactly TTL
                currentTime = baseTime.AddMinutes(ttlMinutes);

                // Trigger cleanup
                cache.CleanupExpired();

                // Assert: Entry should be expired (age == TTL means expired)
                var retrieved = cache.GetSignature(thinkingText);
                
                return (retrieved == null)
                    .Label($"Entry should be expired at exact TTL boundary (TTL={ttlMinutes}min)")
                    .Classify(ttlMinutes <= 30, "short TTL")
                    .Classify(ttlMinutes > 30 && ttlMinutes <= 60, "medium TTL")
                    .Classify(ttlMinutes > 60, "long TTL");
            });
    }

    /// <summary>
    /// Additional property test for TTL expiration: entries just before TTL should remain.
    /// Verifies that entries with age just under TTL are NOT expired.
    /// 
    /// **Feature: antibridge-advanced-features, Property 4: Cache TTL Expiration**
    /// **Validates: Requirements 1.6**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 10, EndSize = 100)]
    public Property CacheTTLExpiration_JustBeforeTTL_EntryRemains()
    {
        var thinkingTextGen = NonEmptyThinkingTextArbitrary().Generator;
        var signatureGen = ValidSignatureArbitrary().Generator;
        var ttlMinutesGen = Gen.Choose(2, 120); // At least 2 minutes so we can be 1 minute before

        return Prop.ForAll(
            thinkingTextGen.ToArbitrary(),
            signatureGen.ToArbitrary(),
            ttlMinutesGen.ToArbitrary(),
            (thinkingText, signature, ttlMinutes) =>
            {
                // Arrange: Set up controllable time
                var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var currentTime = baseTime;
                
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromMinutes(ttlMinutes),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero
                };
                
                using var cache = new SignatureCache(options, () => currentTime);

                // Act: Store entry at base time
                cache.SetSignature(thinkingText, signature);

                // Advance time to just before TTL (1 minute before)
                currentTime = baseTime.AddMinutes(ttlMinutes - 1);

                // Trigger cleanup
                cache.CleanupExpired();

                // Assert: Entry should still be in cache
                var retrieved = cache.GetSignature(thinkingText);
                
                return (retrieved == signature)
                    .Label($"Entry should remain just before TTL (TTL={ttlMinutes}min, age={ttlMinutes - 1}min)")
                    .Classify(ttlMinutes <= 30, "short TTL")
                    .Classify(ttlMinutes > 30 && ttlMinutes <= 60, "medium TTL")
                    .Classify(ttlMinutes > 60, "long TTL");
            });
    }

    /// <summary>
    /// Property 2: Cache Lookup Priority
    /// 
    /// For any thinking block with client signature, if cache contains signature for that thinking text,
    /// output must use cached signature; if cache does not contain, output must use client signature.
    /// 
    /// **Feature: antibridge-advanced-features, Property 2: Cache Lookup Priority**
    /// **Validates: Requirements 1.2, 1.3, 1.4**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 10, EndSize = 100)]
    public Property CacheLookupPriority_CacheHit_UsesCachedSignature()
    {
        var thinkingTextGen = NonEmptyThinkingTextArbitrary().Generator;
        var clientSignatureGen = ValidSignatureArbitrary().Generator;
        var cachedSignatureGen = ValidSignatureArbitrary().Generator;

        return Prop.ForAll(
            thinkingTextGen.ToArbitrary(),
            clientSignatureGen.ToArbitrary(),
            cachedSignatureGen.ToArbitrary(),
            (thinkingText, clientSignature, cachedSignature) =>
            {
                // Skip if signatures are the same (can't distinguish cache hit from miss)
                if (clientSignature == cachedSignature)
                    return true.Label("Skipped: identical signatures");

                // Arrange: Create cache and pre-populate with cached signature
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromHours(1),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero
                };
                using var cache = new SignatureCache(options);
                
                // Pre-populate cache with cached signature for this thinking text
                cache.SetSignature(thinkingText, cachedSignature);

                // Create Claude request with thinking block containing client signature
                var claudeRequest = CreateClaudeRequestWithThinking(thinkingText, clientSignature);
                var requestBytes = Encoding.UTF8.GetBytes(claudeRequest.ToJsonString());

                // Act: Convert with signature cache
                var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, cache);
                var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

                // Assert: Output should use cached signature (not client signature)
                var outputSignature = ExtractThoughtSignatureFromOutput(result);
                
                // The cached signature should be used (after stripping model prefix if present)
                var expectedSignature = ExtractSignatureValue(cachedSignature);
                var clientSignatureValue = ExtractSignatureValue(clientSignature);
                
                var usesCachedSignature = outputSignature == expectedSignature;
                var usesClientSignature = outputSignature == clientSignatureValue;

                return usesCachedSignature
                    .Label($"Cache hit should use cached signature. " +
                           $"Expected: '{expectedSignature}', Got: '{outputSignature}', " +
                           $"Client would be: '{clientSignatureValue}'")
                    .Classify(thinkingText.Length < 50, "short thinking text")
                    .Classify(thinkingText.Length >= 50, "long thinking text");
            });
    }

    /// <summary>
    /// Property 2: Cache Lookup Priority - Cache Miss Case
    /// 
    /// For any thinking block with client signature, if cache does NOT contain signature for that thinking text,
    /// output must use client signature.
    /// 
    /// **Feature: antibridge-advanced-features, Property 2: Cache Lookup Priority**
    /// **Validates: Requirements 1.2, 1.3, 1.4**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 10, EndSize = 100)]
    public Property CacheLookupPriority_CacheMiss_UsesClientSignature()
    {
        var thinkingTextGen = NonEmptyThinkingTextArbitrary().Generator;
        var clientSignatureGen = ValidSignatureArbitrary().Generator;

        return Prop.ForAll(
            thinkingTextGen.ToArbitrary(),
            clientSignatureGen.ToArbitrary(),
            (thinkingText, clientSignature) =>
            {
                // Arrange: Create empty cache (no pre-population)
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromHours(1),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero
                };
                using var cache = new SignatureCache(options);
                
                // Cache is empty - no signature for this thinking text

                // Create Claude request with thinking block containing client signature
                var claudeRequest = CreateClaudeRequestWithThinking(thinkingText, clientSignature);
                var requestBytes = Encoding.UTF8.GetBytes(claudeRequest.ToJsonString());

                // Act: Convert with signature cache (empty)
                var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, cache);
                var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

                // Assert: Output should use client signature (fallback)
                var outputSignature = ExtractThoughtSignatureFromOutput(result);
                var expectedSignature = ExtractSignatureValue(clientSignature);

                return (outputSignature == expectedSignature)
                    .Label($"Cache miss should use client signature. " +
                           $"Expected: '{expectedSignature}', Got: '{outputSignature}'")
                    .Classify(thinkingText.Length < 50, "short thinking text")
                    .Classify(thinkingText.Length >= 50, "long thinking text");
            });
    }

    /// <summary>
    /// Property 2: Cache Lookup Priority - No Cache Provided
    /// 
    /// When no signature cache is provided, output must use client signature.
    /// 
    /// **Feature: antibridge-advanced-features, Property 2: Cache Lookup Priority**
    /// **Validates: Requirements 1.2, 1.3, 1.4**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 10, EndSize = 100)]
    public Property CacheLookupPriority_NoCacheProvided_UsesClientSignature()
    {
        var thinkingTextGen = NonEmptyThinkingTextArbitrary().Generator;
        var clientSignatureGen = ValidSignatureArbitrary().Generator;

        return Prop.ForAll(
            thinkingTextGen.ToArbitrary(),
            clientSignatureGen.ToArbitrary(),
            (thinkingText, clientSignature) =>
            {
                // Arrange: No cache provided (null)
                
                // Create Claude request with thinking block containing client signature
                var claudeRequest = CreateClaudeRequestWithThinking(thinkingText, clientSignature);
                var requestBytes = Encoding.UTF8.GetBytes(claudeRequest.ToJsonString());

                // Act: Convert without signature cache (null)
                var resultBytes = ClaudeToAntigravityRequest.Convert("test-model", requestBytes, false, null);
                var result = JsonNode.Parse(Encoding.UTF8.GetString(resultBytes));

                // Assert: Output should use client signature
                var outputSignature = ExtractThoughtSignatureFromOutput(result);
                var expectedSignature = ExtractSignatureValue(clientSignature);

                return (outputSignature == expectedSignature)
                    .Label($"No cache should use client signature. " +
                           $"Expected: '{expectedSignature}', Got: '{outputSignature}'")
                    .Classify(thinkingText.Length < 50, "short thinking text")
                    .Classify(thinkingText.Length >= 50, "long thinking text");
            });
    }

    #region Property 3: Signature Validation

    /// <summary>
    /// Property 3: Signature Validation - Valid Signatures
    /// 
    /// For any signature string with valid format (non-empty, length 10-10000),
    /// validation function must return true.
    /// 
    /// **Feature: antibridge-advanced-features, Property 3: Signature Validation**
    /// **Validates: Requirements 1.5**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 10, EndSize = 100)]
    public Property SignatureValidation_ValidSignatures_ReturnsTrue()
    {
        // Generator for valid signatures: non-empty, length 10-10000
        var validSignatureGen = Gen.Elements(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=".ToCharArray())
            .ArrayOf()
            .Where(arr => arr.Length >= 10 && arr.Length <= 10000)
            .Select(arr => new string(arr));

        return Prop.ForAll(
            validSignatureGen.ToArbitrary(),
            signature =>
            {
                // Arrange
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromHours(1),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero
                };
                using var cache = new SignatureCache(options);

                // Act
                var isValid = cache.ValidateSignature(signature);

                // Assert: Valid signatures should return true
                return isValid
                    .Label($"Valid signature (length={signature.Length}) should return true")
                    .Classify(signature.Length == 10, "minimum length (10)")
                    .Classify(signature.Length > 10 && signature.Length <= 100, "short signature (11-100)")
                    .Classify(signature.Length > 100 && signature.Length <= 1000, "medium signature (101-1000)")
                    .Classify(signature.Length > 1000, "long signature (>1000)");
            });
    }

    /// <summary>
    /// Property 3: Signature Validation - Empty/Whitespace Strings
    /// 
    /// For any empty or whitespace-only string, validation function must return false.
    /// 
    /// **Feature: antibridge-advanced-features, Property 3: Signature Validation**
    /// **Validates: Requirements 1.5**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 50)]
    public Property SignatureValidation_EmptyOrWhitespace_ReturnsFalse()
    {
        // Generator for empty/whitespace strings (using nullable string explicitly)
        var emptyOrWhitespaceGen = Gen.OneOf<string?>(
            Gen.Constant<string?>(""),                                  // Empty string
            Gen.Constant<string?>(null),                                // Null
            Gen.Elements(' ', '\t', '\n', '\r')                         // Single whitespace chars
                .ArrayOf()
                .Select(arr => (string?)new string(arr))                // Whitespace-only strings
        ).Where(s => s == null || string.IsNullOrWhiteSpace(s));

        return Prop.ForAll(
            emptyOrWhitespaceGen.ToArbitrary(),
            signature =>
            {
                // Arrange
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromHours(1),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero
                };
                using var cache = new SignatureCache(options);

                // Act
                var isValid = cache.ValidateSignature(signature!);

                // Assert: Empty/whitespace signatures should return false
                return (!isValid)
                    .Label($"Empty/whitespace signature should return false: '{signature ?? "null"}'")
                    .Classify(signature == null, "null")
                    .Classify(signature == "", "empty string")
                    .Classify(signature != null && signature != "" && string.IsNullOrWhiteSpace(signature), "whitespace only");
            });
    }

    /// <summary>
    /// Property 3: Signature Validation - Too Short Strings
    /// 
    /// For any non-empty string with length less than 10, validation function must return false.
    /// 
    /// **Feature: antibridge-advanced-features, Property 3: Signature Validation**
    /// **Validates: Requirements 1.5**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 1, EndSize = 9)]
    public Property SignatureValidation_TooShort_ReturnsFalse()
    {
        // Generator for strings with length 1-9 (too short)
        var tooShortGen = Gen.Elements(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=".ToCharArray())
            .ArrayOf()
            .Where(arr => arr.Length >= 1 && arr.Length < 10)
            .Select(arr => new string(arr));

        return Prop.ForAll(
            tooShortGen.ToArbitrary(),
            signature =>
            {
                // Arrange
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromHours(1),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero
                };
                using var cache = new SignatureCache(options);

                // Act
                var isValid = cache.ValidateSignature(signature);

                // Assert: Too short signatures should return false
                return (!isValid)
                    .Label($"Too short signature (length={signature.Length}) should return false")
                    .Classify(signature.Length == 1, "length 1")
                    .Classify(signature.Length >= 2 && signature.Length <= 5, "length 2-5")
                    .Classify(signature.Length >= 6 && signature.Length <= 9, "length 6-9");
            });
    }

    /// <summary>
    /// Property 3: Signature Validation - Too Long Strings
    /// 
    /// For any string with length greater than 10000, validation function must return false.
    /// 
    /// **Feature: antibridge-advanced-features, Property 3: Signature Validation**
    /// **Validates: Requirements 1.5**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 10001, EndSize = 15000)]
    public Property SignatureValidation_TooLong_ReturnsFalse()
    {
        // Generator for strings with length > 10000 (too long)
        // We generate lengths between 10001 and 15000 to test the boundary
        var lengthGen = Gen.Choose(10001, 15000);
        var tooLongGen = lengthGen.SelectMany(length =>
            Gen.Elements(
                    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=".ToCharArray())
                .ArrayOf(length)
                .Select(arr => new string(arr)));

        return Prop.ForAll(
            tooLongGen.ToArbitrary(),
            signature =>
            {
                // Arrange
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromHours(1),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero
                };
                using var cache = new SignatureCache(options);

                // Act
                var isValid = cache.ValidateSignature(signature);

                // Assert: Too long signatures should return false
                return (!isValid)
                    .Label($"Too long signature (length={signature.Length}) should return false")
                    .Classify(signature.Length <= 11000, "slightly over limit (10001-11000)")
                    .Classify(signature.Length > 11000 && signature.Length <= 13000, "moderately over limit (11001-13000)")
                    .Classify(signature.Length > 13000, "significantly over limit (>13000)");
            });
    }

    /// <summary>
    /// Property 3: Signature Validation - Boundary Cases
    /// 
    /// Tests exact boundary values: length 9 (invalid), length 10 (valid), 
    /// length 10000 (valid), length 10001 (invalid).
    /// 
    /// **Feature: antibridge-advanced-features, Property 3: Signature Validation**
    /// **Validates: Requirements 1.5**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 10, EndSize = 100)]
    public Property SignatureValidation_BoundaryValues_CorrectResults()
    {
        // Generator for boundary test cases: (length, expectedValid)
        var boundaryGen = Gen.Elements(
            (9, false),      // Just below minimum
            (10, true),      // Exactly minimum
            (10000, true),   // Exactly maximum
            (10001, false)   // Just above maximum
        );

        return Prop.ForAll(
            boundaryGen.ToArbitrary(),
            testCase =>
            {
                var (length, expectedValid) = testCase;
                
                // Arrange
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromHours(1),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero
                };
                using var cache = new SignatureCache(options);

                // Generate a signature of exact length
                var signature = new string('A', length);

                // Act
                var isValid = cache.ValidateSignature(signature);

                // Assert
                return (isValid == expectedValid)
                    .Label($"Boundary test: length={length}, expected={expectedValid}, actual={isValid}")
                    .Classify(length == 9, "length 9 (just below min)")
                    .Classify(length == 10, "length 10 (exact min)")
                    .Classify(length == 10000, "length 10000 (exact max)")
                    .Classify(length == 10001, "length 10001 (just above max)");
            });
    }

    /// <summary>
    /// Property 3: Signature Validation - Biconditional Property
    /// 
    /// For any signature string, validation returns true if and only if:
    /// - signature is non-empty AND non-whitespace
    /// - signature length >= 10
    /// - signature length <= 10000
    /// 
    /// **Feature: antibridge-advanced-features, Property 3: Signature Validation**
    /// **Validates: Requirements 1.5**
    /// </summary>
    [Property(MaxTest = 100, StartSize = 0, EndSize = 15000)]
    public Property SignatureValidation_Biconditional_TrueIffValidFormat()
    {
        // Generator for arbitrary strings including edge cases (using nullable string explicitly)
        var arbitraryStringGen = Gen.OneOf<string?>(
            Gen.Constant<string?>(""),                                  // Empty
            Gen.Constant<string?>(null),                                // Null
            Gen.Elements(' ', '\t', '\n', '\r')                         // Whitespace chars
                .ArrayOf()
                .Select(arr => (string?)new string(arr)),
            Gen.Choose(1, 20000)                                        // Various lengths
                .SelectMany(len => 
                    Gen.Elements("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray())
                        .ArrayOf(len)
                        .Select(arr => (string?)new string(arr)))
        );

        return Prop.ForAll(
            arbitraryStringGen.ToArbitrary(),
            signature =>
            {
                // Arrange
                var options = new SignatureCacheOptions
                {
                    TTL = TimeSpan.FromHours(1),
                    MaxEntries = 10000,
                    CleanupInterval = TimeSpan.Zero
                };
                using var cache = new SignatureCache(options);

                // Calculate expected validity based on the specification
                var expectedValid = !string.IsNullOrWhiteSpace(signature) &&
                                   signature!.Length >= 10 &&
                                   signature.Length <= 10000;

                // Act
                var actualValid = cache.ValidateSignature(signature!);

                // Assert: Biconditional - actual should match expected
                return (actualValid == expectedValid)
                    .Label($"Biconditional: signature='{TruncateForDisplay(signature)}' (len={signature?.Length ?? 0}), " +
                           $"expected={expectedValid}, actual={actualValid}")
                    .Classify(signature == null, "null")
                    .Classify(signature == "", "empty")
                    .Classify(signature != null && string.IsNullOrWhiteSpace(signature), "whitespace only")
                    .Classify(signature != null && !string.IsNullOrWhiteSpace(signature) && signature.Length < 10, "too short")
                    .Classify(signature != null && signature.Length >= 10 && signature.Length <= 10000, "valid range")
                    .Classify(signature != null && signature.Length > 10000, "too long");
            });
    }

    /// <summary>
    /// Helper to truncate long strings for display in test labels.
    /// </summary>
    private static string TruncateForDisplay(string? s)
    {
        if (s == null) return "null";
        if (s.Length <= 20) return s;
        return s.Substring(0, 17) + "...";
    }

    #endregion

    #region Helper Methods for Property 2

    /// <summary>
    /// Creates a Claude API request JSON with a thinking block.
    /// </summary>
    private static JsonObject CreateClaudeRequestWithThinking(string thinkingText, string signature)
    {
        return new JsonObject
        {
            ["model"] = "claude-3-opus",
            ["max_tokens"] = 1024,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "thinking",
                            ["thinking"] = thinkingText,
                            ["signature"] = signature
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Extracts the thoughtSignature from the Antigravity output JSON.
    /// </summary>
    private static string? ExtractThoughtSignatureFromOutput(JsonNode? result)
    {
        if (result == null) return null;

        // Navigate to: request.contents[0].parts[0].thoughtSignature
        var contents = result["request"]?["contents"] as JsonArray;
        if (contents == null || contents.Count == 0) return null;

        var firstContent = contents[0];
        var parts = firstContent?["parts"] as JsonArray;
        if (parts == null || parts.Count == 0) return null;

        var firstPart = parts[0];
        return firstPart?["thoughtSignature"]?.GetValue<string>();
    }

    /// <summary>
    /// Extracts the signature value, handling model prefix format (e.g., "model#signature").
    /// The ClaudeToAntigravityRequest strips the model prefix when present.
    /// </summary>
    private static string ExtractSignatureValue(string signature)
    {
        if (string.IsNullOrEmpty(signature)) return signature;
        
        var parts = signature.Split('#', 2);
        return parts.Length == 2 ? parts[1] : signature;
    }

    #endregion
}
