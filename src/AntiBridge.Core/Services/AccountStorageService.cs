using System.Text.Json;
using System.Text.Json.Serialization;
using AntiBridge.Core.Models;

namespace AntiBridge.Core.Services;

/// <summary>
/// Storage index containing list of accounts and current account ID
/// </summary>
public class AccountIndex
{
    [JsonPropertyName("accounts")]
    public List<AccountSummary> Accounts { get; set; } = [];

    [JsonPropertyName("current_account_id")]
    public string? CurrentAccountId { get; set; }
}

/// <summary>
/// Summary info for the index (lightweight)
/// </summary>
public class AccountSummary
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("last_used")]
    public long LastUsed { get; set; }
}

/// <summary>
/// Service for persisting accounts to local storage.
/// Stores accounts in ~/.antibridge/ directory.
/// </summary>
public class AccountStorageService
{
    private const string DataDirName = ".antibridge";
    private const string AccountsIndexFile = "accounts.json";
    private const string AccountsDir = "accounts";

    private readonly string _dataDir;
    private readonly string _accountsDir;
    private readonly string _indexPath;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public event Action<string>? OnStatusChanged;
    public event Action<string>? OnError;

    public AccountStorageService()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _dataDir = Path.Combine(home, DataDirName);
        _accountsDir = Path.Combine(_dataDir, AccountsDir);
        _indexPath = Path.Combine(_dataDir, AccountsIndexFile);

        // Ensure directories exist
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_accountsDir);
    }

    /// <summary>
    /// Load account index
    /// </summary>
    public AccountIndex LoadIndex()
    {
        lock (_lock)
        {
            if (!File.Exists(_indexPath))
                return new AccountIndex();

            try
            {
                var content = File.ReadAllText(_indexPath);
                if (string.IsNullOrWhiteSpace(content))
                    return new AccountIndex();

                return JsonSerializer.Deserialize<AccountIndex>(content, JsonOptions) ?? new AccountIndex();
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Failed to load index: {ex.Message}");
                return new AccountIndex();
            }
        }
    }

    /// <summary>
    /// Save account index
    /// </summary>
    private void SaveIndex(AccountIndex index)
    {
        lock (_lock)
        {
            var content = JsonSerializer.Serialize(index, JsonOptions);
            File.WriteAllText(_indexPath, content);
        }
    }

    /// <summary>
    /// Load a specific account by ID
    /// </summary>
    public Account? LoadAccount(string accountId)
    {
        var path = Path.Combine(_accountsDir, $"{accountId}.json");
        if (!File.Exists(path))
            return null;

        try
        {
            var content = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Account>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to load account {accountId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Save account to file
    /// </summary>
    private void SaveAccount(Account account)
    {
        var path = Path.Combine(_accountsDir, $"{account.Id}.json");
        var content = JsonSerializer.Serialize(account, JsonOptions);
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Get all accounts
    /// </summary>
    public List<Account> ListAccounts()
    {
        var index = LoadIndex();
        var accounts = new List<Account>();

        foreach (var summary in index.Accounts)
        {
            var account = LoadAccount(summary.Id);
            if (account != null)
                accounts.Add(account);
        }

        return accounts;
    }

    /// <summary>
    /// Add or update an account
    /// </summary>
    public Account UpsertAccount(string email, string? name, TokenData token)
    {
        lock (_lock)
        {
            var index = LoadIndex();
            var existing = index.Accounts.FirstOrDefault(a => a.Email == email);

            if (existing != null)
            {
                // Update existing
                var account = LoadAccount(existing.Id);
                if (account != null)
                {
                    account.Token = token;
                    account.Name = name ?? account.Name;
                    account.LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    SaveAccount(account);

                    existing.Name = account.Name;
                    existing.LastUsed = account.LastUsed;
                    SaveIndex(index);

                    OnStatusChanged?.Invoke($"Updated account: {email}");
                    return account;
                }
            }

            // Create new account
            var newAccount = new Account
            {
                Id = Guid.NewGuid().ToString(),
                Email = email,
                Name = name,
                Token = token,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            SaveAccount(newAccount);

            index.Accounts.Add(new AccountSummary
            {
                Id = newAccount.Id,
                Email = email,
                Name = name,
                CreatedAt = newAccount.CreatedAt,
                LastUsed = newAccount.LastUsed
            });

            // Set as current if first account
            if (index.CurrentAccountId == null)
                index.CurrentAccountId = newAccount.Id;

            SaveIndex(index);

            OnStatusChanged?.Invoke($"Added account: {email}");
            return newAccount;
        }
    }

    /// <summary>
    /// Delete an account
    /// </summary>
    public bool DeleteAccount(string accountId)
    {
        lock (_lock)
        {
            var index = LoadIndex();
            var removed = index.Accounts.RemoveAll(a => a.Id == accountId) > 0;

            if (!removed) return false;

            // Update current account if needed
            if (index.CurrentAccountId == accountId)
                index.CurrentAccountId = index.Accounts.FirstOrDefault()?.Id;

            SaveIndex(index);

            // Delete account file
            var path = Path.Combine(_accountsDir, $"{accountId}.json");
            if (File.Exists(path))
                File.Delete(path);

            OnStatusChanged?.Invoke("Account deleted");
            return true;
        }
    }

    /// <summary>
    /// Get current account ID
    /// </summary>
    public string? GetCurrentAccountId()
    {
        return LoadIndex().CurrentAccountId;
    }

    /// <summary>
    /// Set current account ID
    /// </summary>
    public void SetCurrentAccountId(string accountId)
    {
        lock (_lock)
        {
            var index = LoadIndex();
            index.CurrentAccountId = accountId;
            SaveIndex(index);
        }
    }

    /// <summary>
    /// Get current account (fully loaded)
    /// </summary>
    public Account? GetCurrentAccount()
    {
        var currentId = GetCurrentAccountId();
        return currentId != null ? LoadAccount(currentId) : null;
    }

    /// <summary>
    /// Update account quota
    /// </summary>
    public void UpdateAccountQuota(string accountId, QuotaData quota)
    {
        lock (_lock)
        {
            var account = LoadAccount(accountId);
            if (account == null) return;

            account.Quota = quota;
            SaveAccount(account);
        }
    }

    /// <summary>
    /// Check if any accounts are saved
    /// </summary>
    public bool HasSavedAccounts()
    {
        return LoadIndex().Accounts.Count > 0;
    }
}
