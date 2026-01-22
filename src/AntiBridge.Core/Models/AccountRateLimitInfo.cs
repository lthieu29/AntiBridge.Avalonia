namespace AntiBridge.Core.Models;

/// <summary>
/// Rate limit information for an account.
/// Used by LoadBalancer to track account availability.
/// </summary>
public class AccountRateLimitInfo
{
    /// <summary>
    /// The account ID
    /// </summary>
    public string AccountId { get; set; } = "";

    /// <summary>
    /// Whether the account is currently rate limited
    /// </summary>
    public bool IsRateLimited { get; set; }

    /// <summary>
    /// When the rate limit started
    /// </summary>
    public DateTime? RateLimitStarted { get; set; }

    /// <summary>
    /// When the rate limit expires
    /// </summary>
    public DateTime? RateLimitExpiry { get; set; }

    /// <summary>
    /// Whether the account has exceeded its quota
    /// </summary>
    public bool IsQuotaExceeded { get; set; }

    /// <summary>
    /// Error message if any
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Check if the account is available for use
    /// </summary>
    public bool IsAvailable => !IsRateLimited && !IsQuotaExceeded;
}
