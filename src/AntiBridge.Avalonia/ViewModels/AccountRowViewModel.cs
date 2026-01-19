using CommunityToolkit.Mvvm.ComponentModel;
using AntiBridge.Core.Models;
using Avalonia.Media;

namespace AntiBridge.Avalonia.ViewModels;

/// <summary>
/// ViewModel for account row in the table
/// </summary>
public partial class AccountRowViewModel : ObservableObject
{
    public string Id { get; }
    public string Email { get; }
    public string? Name { get; }
    public TokenData Token { get; }
    
    // Gemini 3 Pro quota
    public int Gemini3ProPercent { get; private set; }
    public string Gemini3ProText => $"{Gemini3ProPercent}%";
    public double Gemini3ProWidth => Gemini3ProPercent * 0.8; // Scale to fit
    public string Gemini3ProReset { get; private set; } = "";
    public IBrush Gemini3ProColor => GetColorForPercent(Gemini3ProPercent);
    
    // Claude 4.5 quota
    public int Claude45Percent { get; private set; }
    public string Claude45Text => $"{Claude45Percent}%";
    public double Claude45Width => Claude45Percent * 0.8;
    public string Claude45Reset { get; private set; } = "";
    public IBrush Claude45Color => GetColorForPercent(Claude45Percent);
    
    // 403 Forbidden status
    public bool IsForbidden { get; private set; }
    public string ForbiddenBadge => "🔒 403";
    public string ForbiddenTooltip => "Tài khoản bị chặn (403 Forbidden) - Không có quyền truy cập Gemini Code Assist";
    
    // Last sync
    public string LastSyncDate { get; private set; } = "-";
    public string LastSyncTime { get; private set; } = "";
    
    // Row alternating background
    public IBrush RowBackground { get; }
    
    public AccountRowViewModel(Account account, int rowIndex)
    {
        Id = account.Id;
        Email = account.Email;
        Name = account.Name;
        Token = account.Token;
        
        RowBackground = rowIndex % 2 == 0 
            ? new SolidColorBrush(Color.Parse("#16213e"))
            : new SolidColorBrush(Color.Parse("#1a2744"));
        
        UpdateFromQuota(account.Quota);
        UpdateLastSync(account.LastUsed);
    }
    
    public void UpdateFromQuota(QuotaData? quota)
    {
        if (quota == null) return;
        
        // Check for 403 Forbidden status
        IsForbidden = quota.IsForbidden;
        OnPropertyChanged(nameof(IsForbidden));
        
        foreach (var model in quota.Models)
        {
            var name = model.Name.ToLowerInvariant();
            
            // Match Gemini 3 Pro High / 2.5 Pro
            if (name.Contains("gemini") && (name.Contains("3") || name.Contains("2.5")) && name.Contains("pro"))
            {
                Gemini3ProPercent = model.Percentage;
                Gemini3ProReset = FormatResetTime(model.ResetTime);
            }
            // Match Claude Opus 4.5 Thinking
            else if (name.Contains("claude") && (name.Contains("4 5") || name.Contains("opus") || name.Contains("4.5")))
            {
                Claude45Percent = model.Percentage;
                Claude45Reset = FormatResetTime(model.ResetTime);
            }
        }
        
        OnPropertyChanged(nameof(Gemini3ProPercent));
        OnPropertyChanged(nameof(Gemini3ProText));
        OnPropertyChanged(nameof(Gemini3ProWidth));
        OnPropertyChanged(nameof(Gemini3ProReset));
        OnPropertyChanged(nameof(Gemini3ProColor));
        OnPropertyChanged(nameof(Claude45Percent));
        OnPropertyChanged(nameof(Claude45Text));
        OnPropertyChanged(nameof(Claude45Width));
        OnPropertyChanged(nameof(Claude45Reset));
        OnPropertyChanged(nameof(Claude45Color));
    }
    
    public void UpdateLastSync(long timestamp)
    {
        if (timestamp <= 0)
        {
            LastSyncDate = "-";
            LastSyncTime = "";
            return;
        }
        
        var dt = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
        LastSyncDate = dt.ToString("dd/MM/yyyy");
        LastSyncTime = dt.ToString("HH:mm");
        
        OnPropertyChanged(nameof(LastSyncDate));
        OnPropertyChanged(nameof(LastSyncTime));
    }
    
    private static string FormatResetTime(string? resetTime)
    {
        if (string.IsNullOrEmpty(resetTime)) return "";
        
        try
        {
            if (DateTime.TryParse(resetTime, out var dt))
            {
                var diff = dt - DateTime.UtcNow;
                if (diff.TotalHours < 1) return $"R: {diff.Minutes}m";
                if (diff.TotalDays < 1) return $"R: {(int)diff.TotalHours}h {diff.Minutes}m";
                return $"R: {(int)diff.TotalDays}d {diff.Hours}h";
            }
        }
        catch { }
        
        return "";
    }
    
    private static IBrush GetColorForPercent(int percent)
    {
        return percent switch
        {
            >= 70 => new SolidColorBrush(Color.Parse("#22c55e")), // Green
            >= 30 => new SolidColorBrush(Color.Parse("#eab308")), // Yellow
            _ => new SolidColorBrush(Color.Parse("#ef4444"))      // Red
        };
    }
}
