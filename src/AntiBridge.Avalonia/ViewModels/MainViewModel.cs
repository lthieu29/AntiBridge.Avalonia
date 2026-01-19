using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AntiBridge.Core.Models;
using AntiBridge.Core.Services;

namespace AntiBridge.Avalonia.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly OAuthService _oauthService;
    private readonly QuotaService _quotaService;
    private readonly AntigravityDbService _antigravityDb;
    private readonly AccountStorageService _accountStorage;
    private System.Timers.Timer? _autoRefreshTimer;

    // Auto-refresh interval in minutes (matches Antigravity-Manager default)
    private const int AutoRefreshIntervalMinutes = 15;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private Account? _currentAccount;

    [ObservableProperty]
    private List<ModelQuotaViewModel> _modelQuotas = [];

    [ObservableProperty]
    private bool _isAntigravityInstalled;

    [ObservableProperty]
    private List<Account> _allAccounts = [];

    [ObservableProperty]
    private bool _hasMultipleAccounts;

    [ObservableProperty]
    private bool _autoRefreshEnabled = true;

    [ObservableProperty]
    private DateTime? _lastRefreshTime;

    [ObservableProperty]
    private List<AccountRowViewModel> _allAccountsWithQuota = [];

    public string LastRefreshTimeText => LastRefreshTime.HasValue 
        ? $"Last refresh: {LastRefreshTime:HH:mm:ss}" 
        : "";

    public MainViewModel()
    {
        _oauthService = new OAuthService();
        _quotaService = new QuotaService();
        _antigravityDb = new AntigravityDbService();
        _accountStorage = new AccountStorageService();

        _oauthService.OnStatusChanged += msg => StatusMessage = msg;
        _oauthService.OnError += msg => ErrorMessage = msg;
        _quotaService.OnStatusChanged += msg => StatusMessage = msg;
        _quotaService.OnError += msg => ErrorMessage = msg;
        _antigravityDb.OnStatusChanged += msg => StatusMessage = msg;
        _antigravityDb.OnError += msg => ErrorMessage = msg;
        _accountStorage.OnStatusChanged += msg => StatusMessage = msg;
        _accountStorage.OnError += msg => ErrorMessage = msg;

        // Check if Antigravity is installed
        IsAntigravityInstalled = _antigravityDb.IsAntigravityInstalled();

        // Auto-load saved accounts on startup
        _ = LoadSavedAccountsAsync();
    }

    private void StartAutoRefreshTimer()
    {
        if (!AutoRefreshEnabled) return;

        _autoRefreshTimer?.Stop();
        _autoRefreshTimer = new System.Timers.Timer(AutoRefreshIntervalMinutes * 60 * 1000);
        _autoRefreshTimer.Elapsed += async (_, _) => await AutoRefreshQuotaAsync();
        _autoRefreshTimer.AutoReset = true;
        _autoRefreshTimer.Start();

        StatusMessage = $"Auto-refresh enabled (every {AutoRefreshIntervalMinutes} min)";
    }

    private void StopAutoRefreshTimer()
    {
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
    }

    private async Task AutoRefreshQuotaAsync()
    {
        if (AllAccounts.Count == 0 || IsLoading) return;

        try
        {
            // Silent refresh - don't show loading indicator for all accounts
            await RefreshAllQuotasAsync();
            StatusMessage = $"Auto-refreshed at {LastRefreshTime:HH:mm:ss}";
        }
        catch
        {
            // Ignore errors during auto-refresh
        }
    }

    private async Task RefreshQuotaInternalAsync()
    {
        if (CurrentAccount == null) return;

        // Check if token needs refresh
        if (CurrentAccount.Token.IsExpired)
        {
            var newToken = await _oauthService.RefreshAccessTokenAsync(CurrentAccount.Token.RefreshToken);
            if (newToken != null)
            {
                CurrentAccount.Token = newToken;
                _accountStorage.UpsertAccount(CurrentAccount.Email, CurrentAccount.Name, newToken);
            }
        }

        var quota = await _quotaService.FetchQuotaAsync(CurrentAccount.Token.AccessToken);
        if (quota != null)
        {
            CurrentAccount.Quota = quota;
            _accountStorage.UpdateAccountQuota(CurrentAccount.Id, quota);

            ModelQuotas = quota.Models
                .OrderByDescending(m => m.Percentage)
                .Select(m => new ModelQuotaViewModel(m))
                .ToList();
        }
    }

    private async Task LoadSavedAccountsAsync()
    {
        try
        {
            RefreshAccountList();

            if (AllAccounts.Count > 0)
            {
                StatusMessage = "Loading quotas...";
                
                // Refresh all account quotas on startup
                await RefreshAllQuotasAsync();
                
                // Start auto-refresh timer
                StartAutoRefreshTimer();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Auto-login failed: {ex.Message}";
        }
    }

    private void RefreshAccountList()
    {
        AllAccounts = _accountStorage.ListAccounts();
        HasMultipleAccounts = AllAccounts.Count > 1;
        IsLoggedIn = AllAccounts.Count > 0;
        
        // Build table view model
        AllAccountsWithQuota = AllAccounts
            .Select((acc, idx) => new AccountRowViewModel(acc, idx))
            .ToList();
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            StatusMessage = "Starting OAuth flow...";
            var token = await _oauthService.StartOAuthFlowAsync();

            if (token == null)
            {
                ErrorMessage = "Login failed - no token received";
                return;
            }

            StatusMessage = "Getting user info...";
            var userInfo = await _oauthService.GetUserInfoAsync(token.AccessToken);

            if (userInfo == null)
            {
                ErrorMessage = "Failed to get user info";
                return;
            }

            // Create account
            CurrentAccount = new Account
            {
                Id = Guid.NewGuid().ToString(),
                Email = userInfo.Email,
                Name = userInfo.GetDisplayName(),
                Token = token
            };

            // Save to storage
            CurrentAccount = _accountStorage.UpsertAccount(
                CurrentAccount.Email,
                CurrentAccount.Name,
                CurrentAccount.Token
            );
            _accountStorage.SetCurrentAccountId(CurrentAccount.Id);
            RefreshAccountList();

            IsLoggedIn = true;
            StatusMessage = $"Logged in as {userInfo.Email}";

            // Auto-fetch quota and start auto-refresh
            await RefreshQuotaAsync();
            StartAutoRefreshTimer();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Login error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshQuotaAsync()
    {
        if (CurrentAccount == null) return;

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // Check if token needs refresh
            if (CurrentAccount.Token.IsExpired)
            {
                StatusMessage = "Refreshing token...";
                var newToken = await _oauthService.RefreshAccessTokenAsync(CurrentAccount.Token.RefreshToken);
                if (newToken != null)
                {
                    CurrentAccount.Token = newToken;
                }
            }

            StatusMessage = "Fetching quota...";
            var quota = await _quotaService.FetchQuotaAsync(CurrentAccount.Token.AccessToken);

            if (quota == null)
            {
                ErrorMessage = "Failed to fetch quota";
                return;
            }

            CurrentAccount.Quota = quota;

            // Convert to view models for display
            ModelQuotas = quota.Models
                .OrderByDescending(m => m.Percentage)
                .Select(m => new ModelQuotaViewModel(m))
                .ToList();

            StatusMessage = $"Updated {ModelQuotas.Count} models at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Refresh error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Logout()
    {
        StopAutoRefreshTimer();
        CurrentAccount = null;
        ModelQuotas = [];
        IsLoggedIn = false;
        StatusMessage = "Logged out";
    }

    [RelayCommand]
    private async Task RefreshAllQuotasAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var accounts = _accountStorage.ListAccounts();
            int count = 0;

            foreach (var account in accounts)
            {
                StatusMessage = $"Refreshing {account.Email}...";
                
                try
                {
                    // Ensure token is fresh
                    var token = account.Token;
                    if (token.IsExpired)
                    {
                        var newToken = await _oauthService.RefreshAccessTokenAsync(token.RefreshToken);
                        if (newToken != null)
                        {
                            token = newToken;
                            _accountStorage.UpsertAccount(account.Email, account.Name, newToken);
                        }
                    }

                    // Fetch quota
                    var quota = await _quotaService.FetchQuotaAsync(token.AccessToken);
                    if (quota != null)
                    {
                        _accountStorage.UpdateAccountQuota(account.Id, quota);
                        account.Quota = quota;
                        account.UpdateLastUsed();
                        _accountStorage.UpsertAccount(account.Email, account.Name, account.Token);
                    }
                    count++;
                }
                catch { /* Continue with next account */ }
            }

            RefreshAccountList();
            LastRefreshTime = DateTime.Now;
            OnPropertyChanged(nameof(LastRefreshTimeText));
            StatusMessage = $"Refreshed {count} accounts";
            StartAutoRefreshTimer();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Refresh error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAccountAsync(string accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return;

        var account = _accountStorage.LoadAccount(accountId);
        if (account == null) return;

        try
        {
            StatusMessage = $"Refreshing {account.Email}...";

            var token = account.Token;
            if (token.IsExpired)
            {
                var newToken = await _oauthService.RefreshAccessTokenAsync(token.RefreshToken);
                if (newToken != null)
                {
                    token = newToken;
                    _accountStorage.UpsertAccount(account.Email, account.Name, newToken);
                }
            }

            var quota = await _quotaService.FetchQuotaAsync(token.AccessToken);
            if (quota != null)
            {
                _accountStorage.UpdateAccountQuota(account.Id, quota);
                account.UpdateLastUsed();
                _accountStorage.UpsertAccount(account.Email, account.Name, token);
            }

            RefreshAccountList();
            StatusMessage = $"Refreshed {account.Email}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Refresh error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SyncAccountToAntigravity(string accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return;
        if (!IsAntigravityInstalled)
        {
            ErrorMessage = "Antigravity is not installed";
            return;
        }

        var account = _accountStorage.LoadAccount(accountId);
        if (account == null) return;

        try
        {
            var success = _antigravityDb.InjectTokenToAntigravity(account.Token);
            if (success)
            {
                StatusMessage = $"Synced {account.Email}. Restarting Antigravity...";
                
                // Restart Antigravity to apply new token
                var processService = new AntigravityProcessService();
                processService.OnStatusChanged += msg => StatusMessage = msg;
                processService.OnError += msg => ErrorMessage = msg;
                
                await Task.Run(() => processService.RestartAntigravity());
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Sync error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SwitchAccountAsync(string accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return;

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var account = _accountStorage.LoadAccount(accountId);
            if (account == null)
            {
                ErrorMessage = "Account not found";
                return;
            }

            StatusMessage = $"Switching to {account.Email}...";

            // Refresh token
            var freshToken = await _oauthService.RefreshAccessTokenAsync(account.Token.RefreshToken);
            if (freshToken == null)
            {
                ErrorMessage = "Failed to refresh token for this account";
                return;
            }

            account.Token = freshToken;
            _accountStorage.UpsertAccount(account.Email, account.Name, freshToken);
            _accountStorage.SetCurrentAccountId(accountId);

            CurrentAccount = account;
            IsLoggedIn = true;
            StatusMessage = $"Switched to {account.Email}";

            await RefreshQuotaAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Switch error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void DeleteAccount(string accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return;
        
        var isCurrentAccount = CurrentAccount?.Id == accountId;
        
        if (_accountStorage.DeleteAccount(accountId))
        {
            RefreshAccountList();
            StatusMessage = "Account deleted";

            // If deleted current account, clear login state
            if (isCurrentAccount)
            {
                // Try to switch to another account
                var nextAccount = AllAccounts.FirstOrDefault();
                if (nextAccount != null)
                {
                    _ = SwitchAccountAsync(nextAccount.Id);
                }
                else
                {
                    CurrentAccount = null;
                    ModelQuotas = [];
                    IsLoggedIn = false;
                }
            }
        }
    }

    [RelayCommand]
    private async Task ImportFromAntigravityAsync()
    {
        if (!IsAntigravityInstalled)
        {
            ErrorMessage = "Antigravity is not installed";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            StatusMessage = "Reading token from Antigravity...";
            var token = _antigravityDb.ReadTokenFromAntigravity();

            if (token == null || string.IsNullOrEmpty(token.RefreshToken))
            {
                ErrorMessage = "No valid token found in Antigravity";
                return;
            }

            // Refresh the token to get a fresh access token
            StatusMessage = "Refreshing token...";
            var freshToken = await _oauthService.RefreshAccessTokenAsync(token.RefreshToken);
            if (freshToken == null)
            {
                ErrorMessage = "Failed to refresh token from Antigravity";
                return;
            }

            // Get user info
            StatusMessage = "Getting user info...";
            var userInfo = await _oauthService.GetUserInfoAsync(freshToken.AccessToken);
            if (userInfo == null)
            {
                ErrorMessage = "Failed to get user info";
                return;
            }

            // Create account
            CurrentAccount = new Account
            {
                Id = Guid.NewGuid().ToString(),
                Email = userInfo.Email,
                Name = userInfo.GetDisplayName(),
                Token = freshToken
            };

            // Save to storage
            CurrentAccount = _accountStorage.UpsertAccount(
                CurrentAccount.Email,
                CurrentAccount.Name,
                CurrentAccount.Token
            );
            _accountStorage.SetCurrentAccountId(CurrentAccount.Id);
            RefreshAccountList();

            IsLoggedIn = true;
            StatusMessage = $"Imported account: {userInfo.Email}";

            // Auto-fetch quota
            await RefreshQuotaAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Import error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SyncToAntigravity()
    {
        if (!IsAntigravityInstalled)
        {
            ErrorMessage = "Antigravity is not installed";
            return;
        }

        if (CurrentAccount == null)
        {
            ErrorMessage = "No account to sync";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var success = _antigravityDb.InjectTokenToAntigravity(CurrentAccount.Token);
            if (success)
            {
                StatusMessage = $"Token synced to Antigravity! Restart Antigravity to apply.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Sync error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

/// <summary>
/// ViewModel for displaying a single model quota with color coding
/// </summary>
public partial class ModelQuotaViewModel : ObservableObject
{
    public string Name { get; }
    public int Percentage { get; }
    public string ResetTime { get; }

    public string DisplayName => FormatModelName(Name);
    public string PercentageText => $"{Percentage}%";
    
    // Format reset time for display (e.g., "Resets: 2026-01-20 08:00")
    public string ResetTimeDisplay => FormatResetTime(ResetTime);

    // Color coding: green >50%, orange 20-50%, red <20%
    public string ProgressColor => Percentage switch
    {
        >= 50 => "#22c55e",  // green
        >= 20 => "#f97316",  // orange
        _ => "#ef4444"       // red
    };

    public ModelQuotaViewModel(ModelQuota model)
    {
        Name = model.Name;
        Percentage = model.Percentage;
        ResetTime = model.ResetTime;
    }

    private static string FormatModelName(string name)
    {
        // Make model names more readable
        return name
            .Replace("-", " ")
            .Replace("gemini", "Gemini")
            .Replace("claude", "Claude")
            .Replace("pro", "Pro")
            .Replace("flash", "Flash")
            .Replace("sonnet", "Sonnet")
            .Replace("high", "High")
            .Replace("image", "Image");
    }

    private static string FormatResetTime(string resetTime)
    {
        if (string.IsNullOrEmpty(resetTime))
            return string.Empty;

        // Parse ISO 8601 format (e.g., "2026-01-20T01:00:00Z")
        if (DateTime.TryParse(resetTime, out var dt))
        {
            var local = dt.ToLocalTime();
            var diff = local - DateTime.Now;

            if (diff.TotalHours < 1)
                return $"Resets in {diff.Minutes}m";
            else if (diff.TotalHours < 24)
                return $"Resets in {diff.Hours}h {diff.Minutes}m";
            else
                return $"Resets: {local:MMM dd HH:mm}";
        }

        return resetTime;
    }
}
