using System.Collections.Concurrent;
using AntiBridge.Core.Models;

namespace AntiBridge.Core.Services;

/// <summary>
/// Thread-safe load balancer for distributing requests across multiple accounts.
/// Implements round-robin and fill-first strategies with automatic failover.
/// </summary>
public class LoadBalancer : ILoadBalancer
{
    private readonly LoadBalancerOptions _options;
    private readonly ConcurrentDictionary<string, Account> _accounts;
    private readonly ConcurrentDictionary<string, AccountStatus> _statuses;
    private readonly Func<DateTime> _timeProvider;
    private readonly object _roundRobinLock = new();
    private int _roundRobinIndex;

    /// <summary>
    /// Creates a new LoadBalancer with the specified options.
    /// </summary>
    /// <param name="options">Load balancer configuration options</param>
    public LoadBalancer(LoadBalancerOptions? options = null)
        : this(options, () => DateTime.UtcNow)
    {
    }

    /// <summary>
    /// Creates a new LoadBalancer with the specified options and time provider.
    /// Used for testing with controllable time.
    /// </summary>
    /// <param name="options">Load balancer configuration options</param>
    /// <param name="timeProvider">Function to get current time</param>
    public LoadBalancer(LoadBalancerOptions? options, Func<DateTime> timeProvider)
    {
        _options = options ?? new LoadBalancerOptions();
        _accounts = new ConcurrentDictionary<string, Account>();
        _statuses = new ConcurrentDictionary<string, AccountStatus>();
        _timeProvider = timeProvider;
        _roundRobinIndex = 0;
    }

    /// <summary>
    /// Get next available account based on the configured strategy.
    /// Requirements 5.1, 5.2, 5.5
    /// </summary>
    public Account? GetNextAccount()
    {
        // First, check and reset any expired rate limits
        CheckExpiredRateLimits();

        var accountList = _accounts.Values.ToList();
        if (accountList.Count == 0)
            return null;

        return _options.Strategy switch
        {
            LoadBalancingStrategy.RoundRobin => GetNextRoundRobin(accountList),
            LoadBalancingStrategy.FillFirst => GetNextFillFirst(accountList),
            _ => GetNextRoundRobin(accountList)
        };
    }

    /// <summary>
    /// Get next account using round-robin strategy.
    /// Distributes requests evenly across available accounts.
    /// Requirement 5.2
    /// </summary>
    private Account? GetNextRoundRobin(List<Account> accounts)
    {
        var availableAccounts = accounts.Where(a => IsAccountAvailable(a.Id)).ToList();
        
        // Requirement 5.5: If all accounts unavailable, return null
        if (availableAccounts.Count == 0)
            return null;

        lock (_roundRobinLock)
        {
            // Find next available account starting from current index
            for (var i = 0; i < accounts.Count; i++)
            {
                var index = (_roundRobinIndex + i) % accounts.Count;
                var account = accounts[index];
                
                if (IsAccountAvailable(account.Id))
                {
                    _roundRobinIndex = (index + 1) % accounts.Count;
                    UpdateAccountUsage(account.Id);
                    return account;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Get next account using fill-first strategy.
    /// Uses one account until rate limited, then switches.
    /// Requirement 5.2
    /// </summary>
    private Account? GetNextFillFirst(List<Account> accounts)
    {
        // Find first available account
        foreach (var account in accounts)
        {
            if (IsAccountAvailable(account.Id))
            {
                UpdateAccountUsage(account.Id);
                return account;
            }
        }

        // Requirement 5.5: If all accounts unavailable, return null
        return null;
    }

    /// <summary>
    /// Check if an account is available for use.
    /// </summary>
    private bool IsAccountAvailable(string accountId)
    {
        if (!_statuses.TryGetValue(accountId, out var status))
            return true; // No status means available

        return status.IsAvailable;
    }

    /// <summary>
    /// Update account usage statistics.
    /// </summary>
    private void UpdateAccountUsage(string accountId)
    {
        var now = _timeProvider();
        _statuses.AddOrUpdate(
            accountId,
            _ => new AccountStatus
            {
                AccountId = accountId,
                RequestCount = 1,
                LastUsed = now
            },
            (_, existing) =>
            {
                existing.RequestCount++;
                existing.LastUsed = now;
                return existing;
            });
    }

    /// <summary>
    /// Mark an account as rate limited.
    /// Requirements 5.3, 5.6
    /// </summary>
    public void MarkRateLimited(string accountId, TimeSpan? retryAfter = null)
    {
        var now = _timeProvider();
        var duration = retryAfter ?? _options.DefaultRateLimitDuration;
        var expiry = now.Add(duration);

        _statuses.AddOrUpdate(
            accountId,
            _ => new AccountStatus
            {
                AccountId = accountId,
                IsRateLimited = true,
                RateLimitExpiry = expiry,
                LastUsed = now
            },
            (_, existing) =>
            {
                existing.IsRateLimited = true;
                existing.RateLimitExpiry = expiry;
                return existing;
            });
    }

    /// <summary>
    /// Mark an account as quota exceeded.
    /// Requirement 5.4
    /// </summary>
    public void MarkQuotaExceeded(string accountId)
    {
        var now = _timeProvider();

        _statuses.AddOrUpdate(
            accountId,
            _ => new AccountStatus
            {
                AccountId = accountId,
                IsQuotaExceeded = true,
                LastUsed = now
            },
            (_, existing) =>
            {
                existing.IsQuotaExceeded = true;
                return existing;
            });
    }

    /// <summary>
    /// Get status of all accounts.
    /// </summary>
    public IReadOnlyList<AccountStatus> GetAccountStatuses()
    {
        // Ensure all accounts have a status entry
        foreach (var account in _accounts.Values)
        {
            _statuses.GetOrAdd(account.Id, _ => new AccountStatus
            {
                AccountId = account.Id
            });
        }

        return _statuses.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Reset rate limit status for an account.
    /// </summary>
    public void ResetRateLimit(string accountId)
    {
        if (_statuses.TryGetValue(accountId, out var status))
        {
            status.IsRateLimited = false;
            status.RateLimitExpiry = null;
        }
    }

    /// <summary>
    /// Add an account to the load balancer pool.
    /// </summary>
    public void AddAccount(Account account)
    {
        _accounts[account.Id] = account;
        _statuses.GetOrAdd(account.Id, _ => new AccountStatus
        {
            AccountId = account.Id
        });
    }

    /// <summary>
    /// Remove an account from the load balancer pool.
    /// </summary>
    public void RemoveAccount(string accountId)
    {
        _accounts.TryRemove(accountId, out _);
        _statuses.TryRemove(accountId, out _);
    }

    /// <summary>
    /// Check and reset expired rate limits.
    /// Requirement 5.7: When account's rate limit period expires, it becomes available again.
    /// </summary>
    public void CheckExpiredRateLimits()
    {
        var now = _timeProvider();

        foreach (var kvp in _statuses)
        {
            var status = kvp.Value;
            
            // Check if rate limit has expired
            if (status.IsRateLimited && status.RateLimitExpiry.HasValue)
            {
                if (status.RateLimitExpiry.Value <= now)
                {
                    status.IsRateLimited = false;
                    status.RateLimitExpiry = null;
                }
            }
        }
    }

    /// <summary>
    /// Gets the current number of accounts in the pool.
    /// </summary>
    public int AccountCount => _accounts.Count;

    /// <summary>
    /// Gets the number of available accounts.
    /// </summary>
    public int AvailableAccountCount => _accounts.Values.Count(a => IsAccountAvailable(a.Id));
}
