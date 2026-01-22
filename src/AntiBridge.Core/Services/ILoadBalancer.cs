using AntiBridge.Core.Models;

namespace AntiBridge.Core.Services;

/// <summary>
/// Load balancing strategy for distributing requests across accounts.
/// </summary>
public enum LoadBalancingStrategy
{
    /// <summary>
    /// Distribute requests evenly across accounts in round-robin fashion.
    /// </summary>
    RoundRobin,

    /// <summary>
    /// Use one account until rate limited, then switch to next.
    /// </summary>
    FillFirst
}

/// <summary>
/// Account status for load balancing decisions.
/// </summary>
public class AccountStatus
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
    /// When the rate limit expires (if rate limited)
    /// </summary>
    public DateTime? RateLimitExpiry { get; set; }

    /// <summary>
    /// Whether the account has exceeded its quota
    /// </summary>
    public bool IsQuotaExceeded { get; set; }

    /// <summary>
    /// Number of requests made with this account
    /// </summary>
    public int RequestCount { get; set; }

    /// <summary>
    /// When this account was last used
    /// </summary>
    public DateTime LastUsed { get; set; }

    /// <summary>
    /// Check if the account is available for use
    /// </summary>
    public bool IsAvailable => !IsRateLimited && !IsQuotaExceeded;
}

/// <summary>
/// Load balancer for multi-account support.
/// Distributes requests across multiple accounts and handles failover.
/// </summary>
public interface ILoadBalancer
{
    /// <summary>
    /// Get next available account based on the configured strategy.
    /// </summary>
    /// <returns>Account or null if all accounts are rate limited/unavailable</returns>
    Account? GetNextAccount();

    /// <summary>
    /// Mark an account as rate limited.
    /// </summary>
    /// <param name="accountId">Account ID to mark</param>
    /// <param name="retryAfter">Optional retry-after duration from API response</param>
    void MarkRateLimited(string accountId, TimeSpan? retryAfter = null);

    /// <summary>
    /// Mark an account as quota exceeded.
    /// </summary>
    /// <param name="accountId">Account ID to mark</param>
    void MarkQuotaExceeded(string accountId);

    /// <summary>
    /// Get status of all accounts.
    /// </summary>
    IReadOnlyList<AccountStatus> GetAccountStatuses();

    /// <summary>
    /// Reset rate limit status for an account.
    /// </summary>
    /// <param name="accountId">Account ID to reset</param>
    void ResetRateLimit(string accountId);

    /// <summary>
    /// Add an account to the load balancer pool.
    /// </summary>
    /// <param name="account">Account to add</param>
    void AddAccount(Account account);

    /// <summary>
    /// Remove an account from the load balancer pool.
    /// </summary>
    /// <param name="accountId">Account ID to remove</param>
    void RemoveAccount(string accountId);

    /// <summary>
    /// Check and reset expired rate limits.
    /// </summary>
    void CheckExpiredRateLimits();
}

/// <summary>
/// Configuration options for the load balancer.
/// </summary>
public class LoadBalancerOptions
{
    /// <summary>
    /// Load balancing strategy to use.
    /// Default is RoundRobin.
    /// </summary>
    public LoadBalancingStrategy Strategy { get; set; } = LoadBalancingStrategy.RoundRobin;

    /// <summary>
    /// Default rate limit duration if not specified by API.
    /// Default is 1 minute.
    /// </summary>
    public TimeSpan DefaultRateLimitDuration { get; set; } = TimeSpan.FromMinutes(1);
}
